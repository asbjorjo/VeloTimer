using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Duende.AccessTokenManagement.OpenIdConnect;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OAuth.Claims;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using VeloTime.WebUI.Mud;
using VeloTime.WebUI.Mud.Endpoints;
using VeloTime.WebUI.Mud.Services;
using Contracts = VeloTime.Module.Profiles.Client;

namespace VeloTime.Platform.Test;

/// <summary>
/// Regression for the post-login display-preference 403: drives the real OpenIdConnect handler through
/// challenge → form_post callback → code redemption → signed id_token validation → userinfo → claim actions →
/// cookie sign-in through the production session ticket store, then reloads that ticket for the BFF request.
/// Synthetic principals with a hand-written "iss" claim cannot detect claim-action changes.
/// </summary>
public sealed partial class WebUiBffTests
{
    private const string OidcScheme = "VeloTimeOIDC";
    private const string ValidatedIssuer = "https://idp.test/realms/velotime";
    private const string ForgedUserInfoIssuer = "https://forged.example/realms/velotime";
    private const string OidcSubject = "5d3f8f0e-7b5c-4f39-9a51-1c2d3e4f5a6b";
    private const string OidcSession = "keycloak-session-1";
    private static readonly Uri OidcOrigin = new("https://localhost/");

    [Fact]
    public void OidcClaimActions_RemoveOnlyDefaultIssuerDelete_KeepSidAndNeverMapIssuer()
    {
        // Same registration path as Program.cs (Aspire Keycloak extension + ConfigureCookieOidc).
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        builder.Configuration["ConnectionStrings:keycloak"] = "https://idp.test";
        var services = builder.Services;
        services.AddAuthentication().AddCookie().AddKeycloakOpenIdConnect("keycloak", "velotime", OidcScheme, options =>
        {
            options.ClientId = "velotime.webui";
            options.Authority = ValidatedIssuer;
        });
        services.ConfigureCookieOidc(CookieAuthenticationDefaults.AuthenticationScheme, OidcScheme);
        using var host = builder.Build();
        var configured = host.Services.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>().Get(OidcScheme).ClaimActions.Select(Describe).ToArray();
        var defaults = new OpenIdConnectOptions().ClaimActions.Select(Describe).ToArray();

        // Guard the framework assumption that caused the bug; if it changes, revisit the production override.
        Assert.Contains("DeleteClaimAction:iss", defaults);
        Assert.DoesNotContain(configured, action => action.Split(':')[1] == "iss");
        Assert.DoesNotContain(configured, action => action.Split(':')[1] == "sid");
        Assert.Equal(defaults.Where(action => action != "DeleteClaimAction:iss").Append("UniqueJsonKeyClaimAction:realm_access:realm_access"), configured);

        static string Describe(ClaimAction action) => action switch
        {
            JsonKeyClaimAction json => $"{action.GetType().Name}:{action.ClaimType}:{json.JsonKey}",
            _ => $"{action.GetType().Name}:{action.ClaimType}"
        };
    }

    [Fact]
    public async Task OidcCallback_SignedIdTokenIssuerSurvivesTicketReload_DisplayPreferenceCachedWithValidatedIssuer()
    {
        var profiles = PlatformServiceTests.TestProxy.Create<Contracts.IProfilesClient>((method, _) => method.Name switch
        {
            nameof(Contracts.IProfilesClient.GetCurrentDisplayPreferenceAsync) => Snapshot(),
            _ => throw new NotSupportedException(method.Name)
        });
        await using var harness = await OidcHarness.StartAsync(profiles, restorePreFixIssuerDelete: false);
        using var client = CreateClient(harness.App, origin: OidcOrigin);

        await harness.SignInThroughOidcCallbackAsync(client);

        for (var request = 0; request < 2; request++)
        {
            using var response = await client.GetAsync("api/profile/preferences/display");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(response.Headers.CacheControl!.NoStore);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("ft", body.GetProperty("distanceUnit").GetString());
            Assert.Equal("Europe/Oslo", body.GetProperty("timeZoneId").GetString());
        }

        Assert.Single(harness.Tickets.Stored);
        Assert.True(harness.Tickets.Reloaded.Count >= 2);
        foreach (var reloaded in harness.Tickets.Reloaded)
        {
            // Claims are asserted on tickets deserialized from the protected session-store payload.
            var iss = Assert.Single(reloaded.Principal.FindAll("iss"));
            Assert.Equal(ValidatedIssuer, iss.Value);
            Assert.Equal(ValidatedIssuer, iss.Issuer); // emitted by the id_token validator, not copied from userinfo
            Assert.DoesNotContain(reloaded.Principal.Claims, claim => claim.Value == ForgedUserInfoIssuer || claim.Issuer == ForgedUserInfoIssuer);
            Assert.Equal(OidcSubject, Assert.Single(reloaded.Principal.FindAll("sub")).Value);
            Assert.Equal(OidcSession, Assert.Single(reloaded.Principal.FindAll("sid")).Value);
            foreach (var deleted in new[] { "nonce", "aud", "azp", "iat", "nbf", "exp", "at_hash" })
                Assert.Empty(reloaded.Principal.FindAll(deleted));
            Assert.NotNull(reloaded.Principal.FindFirst("realm_access")); // userinfo mapping still applies
        }

        // Second request is a cache hit, partitioned by the validated (not userinfo) issuer.
        Assert.Single(((PlatformServiceTests.TestProxy)(object)profiles).Calls);
        Assert.Equal(2, harness.Cache.Reads.Count);
        Assert.All(harness.Cache.Reads, read =>
        {
            Assert.Contains(WebUiCache.PreferenceTag(ValidatedIssuer, OidcSubject, "display"), read.Tags);
            Assert.DoesNotContain(WebUiCache.PreferenceTag(ForgedUserInfoIssuer, OidcSubject, "display"), read.Tags);
        });
    }

    [Fact]
    public async Task OidcCallback_PreFixTicketWithoutIssuer_StillForbiddenWithoutCacheReadOrUpstream()
    {
        var profiles = PlatformServiceTests.TestProxy.Create<Contracts.IProfilesClient>((_, _) => throw new InvalidOperationException("must not call upstream"));
        // Recreates sessions persisted before the fix: the framework default DeleteClaim("iss") is reinstated.
        await using var harness = await OidcHarness.StartAsync(profiles, restorePreFixIssuerDelete: true);
        using var client = CreateClient(harness.App, origin: OidcOrigin);

        await harness.SignInThroughOidcCallbackAsync(client);

        using var response = await client.GetAsync("api/profile/preferences/display");
        // Authenticated (token store keyed by sub/sid still resolves) but fail-closed in the cache: 403, not 401.
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Single(harness.Tickets.Stored);
        var reloaded = Assert.Single(harness.Tickets.Reloaded);
        Assert.Empty(reloaded.Principal.FindAll("iss"));
        Assert.Equal(OidcSubject, reloaded.Principal.FindFirst("sub")?.Value);
        Assert.Equal(OidcSession, reloaded.Principal.FindFirst("sid")?.Value);
        Assert.Empty(harness.Cache.Reads);
        Assert.Empty(((PlatformServiceTests.TestProxy)(object)profiles).Calls);
    }

    private sealed class OidcHarness : IAsyncDisposable
    {
        private readonly RsaSecurityKey signingKey = new(RSA.Create(2048)) { KeyId = "test-signing-key" };
        private readonly string accessToken = "access-" + Guid.NewGuid().ToString("N");
        private string? expectedNonce;
        internal WebApplication App { get; private set; } = null!;
        internal RecordingTicketStore Tickets { get; private set; } = null!;
        internal RecordingHybridCache Cache { get; private set; } = null!;
        internal int TokenRequests { get; private set; }
        internal int UserInfoRequests { get; private set; }

        internal static async Task<OidcHarness> StartAsync(Contracts.IProfilesClient profiles, bool restorePreFixIssuerDelete)
        {
            var harness = new OidcHarness();
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Logging.SetMinimumLevel(LogLevel.Critical);
            builder.Configuration["ConnectionStrings:keycloak"] = "https://idp.test";
            builder.Services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                    options.DefaultChallengeScheme = OidcScheme;
                })
                .AddKeycloakOpenIdConnect("keycloak", "velotime", OidcScheme, options =>
                {
                    // Mirrors Program.cs; discovery is replaced by static, in-process metadata and backchannel.
                    options.ClientId = "velotime.webui";
                    options.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                    options.ResponseType = OpenIdConnectResponseType.Code;
                    options.TokenValidationParameters.NameClaimType = "preferred_username";
                    options.Scope.Add("openid");
                    options.Scope.Add("profile");
                    options.Scope.Add("velotime");
                    options.SaveTokens = true;
                    options.Configuration = new OpenIdConnectConfiguration
                    {
                        Issuer = ValidatedIssuer,
                        AuthorizationEndpoint = ValidatedIssuer + "/protocol/openid-connect/auth",
                        TokenEndpoint = ValidatedIssuer + "/protocol/openid-connect/token",
                        UserInfoEndpoint = ValidatedIssuer + "/protocol/openid-connect/userinfo",
                        EndSessionEndpoint = ValidatedIssuer + "/protocol/openid-connect/logout"
                    };
                    options.Configuration.SigningKeys.Add(new RsaSecurityKey(harness.signingKey.Rsa.ExportParameters(false)) { KeyId = harness.signingKey.KeyId });
                })
                .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme);
            builder.Services.ConfigureCookieOidc(CookieAuthenticationDefaults.AuthenticationScheme, OidcScheme);
            // Aspire assigns a service-discovery backchannel; replace it last so token/userinfo never leave the process.
            builder.Services.PostConfigure<OpenIdConnectOptions>(OidcScheme, options => options.Backchannel = new HttpClient(new StubHandler(harness.Respond)));
            if (restorePreFixIssuerDelete)
                builder.Services.PostConfigure<OpenIdConnectOptions>(OidcScheme, options => options.ClaimActions.DeleteClaim("iss"));

            builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
            AddBffServices(builder.Services, profiles: profiles);
            DecorateHybridCache(builder.Services);
            builder.Services.AddSingleton<ServerSideTokenStore>();
            builder.Services.AddSingleton<IUserTokenStore>(provider => provider.GetRequiredService<ServerSideTokenStore>());
            builder.Services.AddSingleton<DistributedTicketStore>();
            builder.Services.AddSingleton(provider => new RecordingTicketStore(provider.GetRequiredService<DistributedTicketStore>()));
            builder.Services.AddSingleton<ITicketStore>(provider => provider.GetRequiredService<RecordingTicketStore>());
            builder.Services.AddTransient<CookieEvents>();
            builder.Services.AddTransient<OidcEvents>();

            var app = builder.Build();
            app.UseAuthentication();
            app.UseAuthorization();
            app.UseAntiforgery();
            app.MapGroup("/authentication").MapLoginAndLogout();
            app.MapBffEndpoints();
            await app.StartAsync();
            harness.App = app;
            harness.Tickets = app.Services.GetRequiredService<RecordingTicketStore>();
            harness.Cache = app.Services.GetRequiredService<RecordingHybridCache>();
            return harness;
        }

        internal async Task SignInThroughOidcCallbackAsync(HttpClient client)
        {
            using var challenge = await client.GetAsync("authentication/login?returnUrl=/");
            Assert.Equal(HttpStatusCode.Redirect, challenge.StatusCode);
            var authorize = challenge.Headers.Location!;
            Assert.StartsWith(ValidatedIssuer + "/protocol/openid-connect/auth", authorize.GetLeftPart(UriPartial.Path));
            var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(authorize.Query);
            expectedNonce = query["nonce"].ToString();
            Assert.False(string.IsNullOrEmpty(expectedNonce));
            Assert.Equal("form_post", query["response_mode"].ToString());

            using var callback = await client.PostAsync(new Uri(query["redirect_uri"].ToString()), new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["code"] = "authorization-code",
                ["state"] = query["state"].ToString()
            }));
            Assert.True(callback.StatusCode == HttpStatusCode.Redirect,
                $"OIDC callback failed with {(int)callback.StatusCode}: {await callback.Content.ReadAsStringAsync()}");
            Assert.Equal("/", callback.Headers.Location!.OriginalString);
            Assert.Equal(1, TokenRequests);
            Assert.Equal(1, UserInfoRequests);
        }

        private async Task<HttpResponseMessage> Respond(HttpRequestMessage request)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Post && path.EndsWith("/token"))
            {
                TokenRequests++;
                var form = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(await request.Content!.ReadAsStringAsync());
                Assert.Equal("authorization_code", form["grant_type"].ToString());
                Assert.Equal("authorization-code", form["code"].ToString());
                Assert.False(string.IsNullOrEmpty(form["code_verifier"].ToString()));
                return Json(new Dictionary<string, object>
                {
                    ["access_token"] = accessToken,
                    ["token_type"] = "Bearer",
                    ["expires_in"] = "300",
                    ["refresh_token"] = "refresh-" + Guid.NewGuid().ToString("N"),
                    ["scope"] = "openid profile velotime offline_access",
                    ["id_token"] = CreateIdToken()
                });
            }

            if (request.Method == HttpMethod.Get && path.EndsWith("/userinfo"))
            {
                UserInfoRequests++;
                Assert.Equal(accessToken, request.Headers.Authorization?.Parameter);
                return Json(new Dictionary<string, object>
                {
                    ["sub"] = OidcSubject,
                    ["iss"] = ForgedUserInfoIssuer,
                    ["preferred_username"] = "rider",
                    ["realm_access"] = new { roles = new[] { "user" } }
                });
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private string CreateIdToken()
        {
            var now = DateTime.UtcNow;
            var hash = SHA256.HashData(Encoding.ASCII.GetBytes(accessToken));
            return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
            {
                Issuer = ValidatedIssuer,
                Audience = "velotime.webui",
                IssuedAt = now,
                NotBefore = now,
                Expires = now.AddMinutes(5),
                SigningCredentials = new SigningCredentials(signingKey, SecurityAlgorithms.RsaSha256),
                Claims = new Dictionary<string, object>
                {
                    ["sub"] = OidcSubject,
                    ["sid"] = OidcSession,
                    ["nonce"] = expectedNonce!,
                    ["azp"] = "velotime.webui",
                    ["at_hash"] = Base64UrlEncoder.Encode(hash[..(hash.Length / 2)])
                }
            });
        }

        private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json")
        };

        private static void DecorateHybridCache(IServiceCollection services)
        {
            var original = services.Last(descriptor => descriptor.ServiceType == typeof(HybridCache));
            services.Remove(original);
            services.AddSingleton(provider => new RecordingHybridCache((HybridCache)(original.ImplementationInstance
                ?? original.ImplementationFactory?.Invoke(provider)
                ?? ActivatorUtilities.CreateInstance(provider, original.ImplementationType!))));
            services.AddSingleton<HybridCache>(provider => provider.GetRequiredService<RecordingHybridCache>());
        }

        public async ValueTask DisposeAsync()
        {
            if (App is not null) await App.DisposeAsync();
            signingKey.Rsa.Dispose();
        }
    }

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => respond(request);
    }

    private sealed class RecordingTicketStore(ITicketStore inner) : ITicketStore
    {
        internal List<AuthenticationTicket> Stored { get; } = [];
        internal List<AuthenticationTicket> Reloaded { get; } = [];

        public async Task<string> StoreAsync(AuthenticationTicket ticket)
        {
            lock (Stored) Stored.Add(ticket);
            return await inner.StoreAsync(ticket);
        }

        public Task RenewAsync(string key, AuthenticationTicket ticket) => inner.RenewAsync(key, ticket);

        public async Task<AuthenticationTicket?> RetrieveAsync(string key)
        {
            var ticket = await inner.RetrieveAsync(key);
            if (ticket is not null) lock (Reloaded) Reloaded.Add(ticket);
            return ticket;
        }

        public Task RemoveAsync(string key) => inner.RemoveAsync(key);
    }

    internal sealed class RecordingHybridCache(HybridCache inner) : HybridCache
    {
        internal List<(string Key, string[] Tags)> Reads { get; } = [];

        public override ValueTask<T> GetOrCreateAsync<TState, T>(string key, TState state, Func<TState, CancellationToken, ValueTask<T>> factory,
            HybridCacheEntryOptions? options = null, IEnumerable<string>? tags = null, CancellationToken cancellationToken = default)
        {
            lock (Reads) Reads.Add((key, tags?.ToArray() ?? []));
            return inner.GetOrCreateAsync(key, state, factory, options, tags, cancellationToken);
        }

        public override ValueTask SetAsync<T>(string key, T value, HybridCacheEntryOptions? options = null, IEnumerable<string>? tags = null,
            CancellationToken cancellationToken = default) => inner.SetAsync(key, value, options, tags, cancellationToken);

        public override ValueTask RemoveAsync(string key, CancellationToken cancellationToken = default) => inner.RemoveAsync(key, cancellationToken);

        public override ValueTask RemoveByTagAsync(string tag, CancellationToken cancellationToken = default) => inner.RemoveByTagAsync(tag, cancellationToken);
    }
}
