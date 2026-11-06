using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Routing;
using Microsoft.OpenApi;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;
using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.StackExchangeRedis;
using StackExchange.Redis;
using VeloTime.Module.Notifications.Api.Identity;
using VeloTime.Module.Notifications.Interface;
using VeloTime.Module.Notifications.Interface.Identity;

namespace VeloTime.Module.Notifications.Api;

public static class HostingExtensions
{
    public const string DataProtectionApplicationName = "VeloTime.Notifications";
    public const string DataProtectionRedisKey = "velotime:notifications-api:dataprotection-keys";

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

    public static IHostApplicationBuilder AddNotificationsDataProtection(this IHostApplicationBuilder builder)
    {
        // AddModuleCache registers the shared Redis multiplexer used by the durable key ring.
        builder.AddModuleCache();
        builder.Services.AddDataProtection().SetApplicationName(DataProtectionApplicationName);
        builder.Services.AddOptions<KeyManagementOptions>()
            .Configure<IServiceProvider>((options, services) =>
                options.XmlRepository = new RedisXmlRepository(
                    () => services.GetRequiredService<IConnectionMultiplexer>().GetDatabase(),
                    DataProtectionRedisKey));
        return builder;
    }

}
