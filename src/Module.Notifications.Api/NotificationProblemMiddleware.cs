using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using VeloTime.Module.Notifications.Interface.Identity;
using VeloTime.Module.Notifications.Service;

namespace VeloTime.Module.Notifications.Api;

public sealed class NotificationProblemMiddleware(RequestDelegate next, ILogger<NotificationProblemMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        context.Response.Headers.CacheControl = "no-store";
        try
        {
            if (context.Request.Headers.ContainsKey(NotificationSessionAssertion.HeaderName) &&
                !context.Request.Headers.ContainsKey("Authorization"))
                throw new NotificationException(401, "notifications.authentication_required");
            // Supplied invalid credentials never downgrade even on AllowAnonymous.
            if (context.Request.Headers.ContainsKey("Authorization"))
            {
                var authentication = await context.AuthenticateAsync(JwtBearerDefaults.AuthenticationScheme);
                if (!authentication.Succeeded)
                    throw new NotificationException(401, "notifications.authentication_required");
                context.User = authentication.Principal!;
            }
            await next(context);
        }
        catch (NotificationException e)
        {
            await Write(context, e.StatusCode, e.Code);
        }
        catch (BadHttpRequestException e)
        {
            await Write(context, e.StatusCode, "notifications.invalid_content");
        }
        catch (DbUpdateConcurrencyException)
        {
            await Write(context, 409, Interface.NotificationErrorCodes.VersionConflict);
        }
        catch (Exception e) when (DependencyFailure(e))
        {
            // Never log database exception text: provider details may contain target/login IDs.
            logger.LogError("Notifications dependency failure ({ExceptionType})", e.GetType().Name);
            await Write(context, 503, "notifications.dependency_unavailable");
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogError("Unexpected Notifications failure ({ExceptionType})", e.GetType().Name);
            await Write(context, 500, "notifications.request_failed");
        }
    }

    private static bool DependencyFailure(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
            if (current is NpgsqlException or DbUpdateException or TimeoutException) return true;
        return false;
    }

    private async Task Write(HttpContext context, int status, string code)
    {
        logger.LogWarning("Notifications request failed with {StatusCode} {ProblemCode}", status, code);
        if (context.Response.HasStarted) throw new InvalidOperationException("Response already started.");
        if (status == 401) context.Response.Headers.WWWAuthenticate = "Bearer";
        await Results.Problem(statusCode: status, title: code, extensions: new Dictionary<string, object?> { ["code"] = code })
            .ExecuteAsync(context);
    }
}
