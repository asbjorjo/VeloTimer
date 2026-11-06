using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using VeloTime.Module.Notifications;
using VeloTime.Module.Notifications.Api;
using VeloTime.Module.Notifications.Api.Endpoints;
using VeloTime.Module.Notifications.Storage;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.AddNotificationsApi();
builder.AddModuleNotifications();
builder.AddNotificationsDataProtection();
builder.Services.AddHealthChecks()
    .AddCheck<NotificationsDatabaseHealthCheck>("notifications-storage", tags: ["ready"])
    .AddCheck<NotificationsDataProtectionHealthCheck>("notifications-data-protection", tags: ["ready"]);
var app = builder.Build();
app.UseMiddleware<NotificationProblemMiddleware>();
app.UseStatusCodePages(async context =>
{
    var status = context.HttpContext.Response.StatusCode;
    var code = status switch
    {
        400 => "notifications.invalid_content",
        401 => "notifications.authentication_required",
        403 => "notifications.forbidden",
        404 => "notifications.not_found",
        _ => "notifications.request_failed"
    };
    context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>()
        .CreateLogger("Notifications.Problems").LogWarning("Notifications request failed with {StatusCode} {ProblemCode}", status, code);
    await Results.Problem(statusCode: status, title: code, extensions: new Dictionary<string, object?> { ["code"] = code })
        .ExecuteAsync(context.HttpContext);
});
app.UseAuthentication();
app.UseAuthorization();
app.MapDefaultEndpoints();
if (!app.Environment.IsDevelopment())
{
    app.MapHealthChecks("/health");
    app.MapHealthChecks("/alive", new HealthCheckOptions { Predicate = c => c.Tags.Contains("live") });
}
app.MapNotificationEndpoints();
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(o => o.SwaggerEndpoint("/openapi/v1.json", "VeloTime Notifications API V1"));
}
app.UseHttpsRedirection();
app.Run();

public partial class Program;

internal sealed class NotificationsDatabaseHealthCheck(IServiceScopeFactory scopes)
    : Microsoft.Extensions.Diagnostics.HealthChecks.IHealthCheck
{
    public async Task<Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult> CheckHealthAsync(
        Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
            await db.SnapshotRevisions.Select(r => r.Revision).SingleAsync(cancellationToken);
            return Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy();
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Unhealthy("Notifications storage unavailable.");
        }
    }
}

internal sealed class NotificationsDataProtectionHealthCheck(
    Microsoft.AspNetCore.DataProtection.IDataProtectionProvider protection,
    StackExchange.Redis.IConnectionMultiplexer redis)
    : Microsoft.Extensions.Diagnostics.HealthChecks.IHealthCheck
{
    public async Task<Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult> CheckHealthAsync(
        Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await redis.GetDatabase().PingAsync();
            _ = protection.CreateProtector("Notifications.Snapshot.v1")
                .Protect(System.Text.Encoding.UTF8.GetBytes("readiness"));
            return Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy();
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Unhealthy("Notifications protected snapshot key ring unavailable.");
        }
    }
}
