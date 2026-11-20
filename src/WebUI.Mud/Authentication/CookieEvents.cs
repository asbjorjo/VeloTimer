// Copyright (c) Duende Software. All rights reserved.
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using Duende.AccessTokenManagement.OpenIdConnect;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace VeloTime.WebUI.Mud;

public class CookieEvents : CookieAuthenticationEvents
{
    private readonly IUserTokenStore _store;

    public CookieEvents(IUserTokenStore store) => _store = store;

    public override Task RedirectToLogin(RedirectContext<CookieAuthenticationOptions> context)
    {
        if (!context.Request.Path.StartsWithSegments("/api"))
        {
            return base.RedirectToLogin(context);
        }

        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        if (IsNotifications(context.Request.Path))
        {
            context.Response.Headers.CacheControl = "no-store";
            return Results.Problem(statusCode: 401, extensions: new Dictionary<string, object?>
                { ["code"] = "notifications.authentication_required" }).ExecuteAsync(context.HttpContext);
        }
        return Task.CompletedTask;
    }

    public override Task RedirectToAccessDenied(RedirectContext<CookieAuthenticationOptions> context)
    {
        if (!context.Request.Path.StartsWithSegments("/api"))
        {
            return base.RedirectToAccessDenied(context);
        }

        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        if (IsNotifications(context.Request.Path))
        {
            context.Response.Headers.CacheControl = "no-store";
            return Results.Problem(statusCode: 403, extensions: new Dictionary<string, object?>
                { ["code"] = "notifications.forbidden" }).ExecuteAsync(context.HttpContext);
        }
        return Task.CompletedTask;
    }

    public override async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        var token = await _store.GetTokenAsync(context.Principal!);
        if (!token.Succeeded)
        {
            context.RejectPrincipal();
        }

        await base.ValidatePrincipal(context);
    }

    public override async Task SigningOut(CookieSigningOutContext context)
    {
        await context.HttpContext.RevokeRefreshTokenAsync();

        await base.SigningOut(context);
    }

    private static bool IsNotifications(PathString path) =>
        path.StartsWithSegments("/api/notifications") || path.StartsWithSegments("/api/admin/notifications");
}
