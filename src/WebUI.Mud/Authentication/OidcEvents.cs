// Copyright (c) Duende Software. All rights reserved.
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using Duende.AccessTokenManagement;
using Duende.AccessTokenManagement.OpenIdConnect;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;

namespace VeloTime.WebUI.Mud;

public class OidcEvents : OpenIdConnectEvents
{
    private readonly IUserTokenStore _store;

    public OidcEvents(IUserTokenStore store) => _store = store;

    public override async Task RedirectToIdentityProvider(RedirectContext context)
    {
        var action = context.Properties.GetParameter<string>("kc_action");

        if (action != null)
        {
            var token = await context.HttpContext.GetTokenAsync("access_token");
            if (token != null) context.ProtocolMessage.AccessToken = token;
            context.ProtocolMessage.SetParameter("kc_action", action);
        }

        await base.RedirectToIdentityProvider(context);
    }

    public override async Task TokenValidated(TokenValidatedContext context)
    {
        var exp = DateTimeOffset.UtcNow.AddSeconds(double.Parse(context.TokenEndpointResponse!.ExpiresIn));

        await _store.StoreTokenAsync(context.Principal!, new UserToken
        {
            AccessToken = AccessToken.Parse(context.TokenEndpointResponse.AccessToken),
            AccessTokenType = AccessTokenType.Parse(context.TokenEndpointResponse.TokenType),
            RefreshToken = RefreshToken.Parse(context.TokenEndpointResponse.RefreshToken),
            Scope = Scope.Parse(context.TokenEndpointResponse.Scope),

            // The clientid isn't always returned from the protocol response.
            // Either get it from IOptions<OpenIdConnectOptions> or hard code it like below. 
            ClientId = ClientId.Parse(context.TokenEndpointResponse.ClientId ?? "interactive.confidential.short"),
            IdentityToken = IdentityToken.Parse(context.TokenEndpointResponse.IdToken),
            Expiration = exp,
        });

        await base.TokenValidated(context);
    }
}
