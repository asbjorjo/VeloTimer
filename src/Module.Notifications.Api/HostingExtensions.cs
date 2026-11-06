using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Routing;
using Microsoft.OpenApi;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;
using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using VeloTime.Module.Notifications.Api.Identity;
using VeloTime.Module.Notifications.Interface;
using VeloTime.Module.Notifications.Interface.Identity;

namespace VeloTime.Module.Notifications.Api;

public static class HostingExtensions
{
    public static IHostApplicationBuilder AddNotificationsApi(this IHostApplicationBuilder builder)
    {
        builder.AddModuleAuthentication();
        builder.Services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, o => o.MapInboundClaims = false);
        builder.Services.Configure<RouteHandlerOptions>(o => o.ThrowOnBadRequest = true);
        builder.Services.AddAuthorizationBuilder().AddPolicy(NotificationAuthorization.AdministrationPolicy,
            p => p.RequireAuthenticatedUser().RequireClaim(NotificationAuthorization.PermissionClaimType,
                NotificationAuthorization.GlobalAdministrationPermission));
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<INotificationReaderIdentityAccessor, NotificationReaderIdentityAccessor>();
        var options = builder.Services.AddOptions<NotificationSessionAssertionOptions>()
            .Bind(builder.Configuration.GetSection(NotificationSessionAssertion.ConfigurationSection))
            .Validate(ValidOptions, "Configure trusted Notifications SessionAssertion issuer, audience and RSA public keys.");
        if (System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name != "GetDocument.Insider")
            options.ValidateOnStart();
        builder.Services.AddProblemDetails();
        builder.Services.ConfigureHttpJsonOptions(o =>
            o.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow);
        builder.Services.AddOpenApi(o =>
        {
            o.AddSchemaTransformer((schema, context, ct) =>
            {
                if (context.JsonTypeInfo.Type.IsEnum)
                {
                    schema.Type = JsonSchemaType.String;
                    schema.Enum = Enum.GetNames(context.JsonTypeInfo.Type).Select(name =>
                        (JsonNode)JsonValue.Create(context.JsonTypeInfo.Type.GetField(name)!
                            .GetCustomAttribute<JsonStringEnumMemberNameAttribute>()!.Name)!).ToList();
                }
                if (context.JsonTypeInfo.Type == typeof(ProblemDetails))
                {
                    schema.AdditionalPropertiesAllowed = true;
                    schema.Properties ??= new Dictionary<string, IOpenApiSchema>();
                    schema.Properties["code"] = new OpenApiSchema { Type = JsonSchemaType.String };
                    schema.Required ??= new HashSet<string>();
                    schema.Required.Add("code");
                }
                if (context.JsonTypeInfo.Type == typeof(Interface.Data.CreateNotificationRequest) ||
                    context.JsonTypeInfo.Type == typeof(Interface.Data.UpdateNotificationRequest))
                    schema.Required?.Remove("endsAtUtc");
                return Task.CompletedTask;
            });
            o.AddOperationTransformer((operation, context, ct) =>
            {
                operation.Parameters ??= [];
                foreach (var parameter in operation.Parameters.OfType<OpenApiParameter>())
                {
                    if (parameter.Name == "scope") parameter.Required = true;
                    if (parameter.Name == "pageSize" && parameter.Schema is OpenApiSchema page)
                    {
                        page.Minimum = "1";
                        page.Maximum = NotificationLimits.MaximumPageSize.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    }
                }
                operation.Parameters.Add(new OpenApiParameter
                {
                    Name = NotificationSessionAssertion.HeaderName, In = ParameterLocation.Header,
                    Required = false, Description = "BFF-only signed application-login assertion; required for all authenticated requests.",
                    Schema = new OpenApiSchema { Type = JsonSchemaType.String }
                });
                if (context.Description.RelativePath != "api/notifications/active")
                    operation.Security = [new OpenApiSecurityRequirement
                    {
                        [new OpenApiSecuritySchemeReference("Bearer", context.Document)] = []
                    }];
                return Task.CompletedTask;
            });
            o.AddDocumentTransformer((document, context, ct) =>
            {
                document.Components ??= new OpenApiComponents();
                document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
                document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
                    { Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT" };
                return Task.CompletedTask;
            });
        });
        return builder;
    }

    private static bool ValidOptions(NotificationSessionAssertionOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Issuer) || string.IsNullOrWhiteSpace(options.Audience) ||
            options.MaximumLifetimeSeconds is < 1 or > NotificationSessionAssertion.MaximumLifetimeSeconds ||
            options.ClockSkewSeconds is < 0 or > NotificationSessionAssertion.MaximumClockSkewSeconds ||
            options.PublicKeys.Count == 0) return false;
        try
        {
            foreach (var (kid, pem) in options.PublicKeys)
            {
                if (kid.Length is < 1 or > 64 || !kid.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_') ||
                    !pem.Contains("-----BEGIN PUBLIC KEY-----", StringComparison.Ordinal) ||
                    pem.Contains("PRIVATE KEY", StringComparison.Ordinal)) return false;
                using var rsa = RSA.Create();
                rsa.ImportFromPem(pem);
                if (rsa.KeySize < 2048) return false;
            }
            return true;
        }
        catch (Exception e) when (e is CryptographicException or ArgumentException) { return false; }
    }
}
