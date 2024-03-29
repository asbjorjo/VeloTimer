﻿using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace VeloTimerWeb.Client.Services
{
    public class VeloTimerAuthorizationMessageHandler : DelegatingHandler, IDisposable
    {
        private readonly IAccessTokenProvider _provider;
        private readonly AuthenticationStateProvider _authStateProvider;
        private readonly IConfiguration _config;
        private readonly NavigationManager _navigation;
        private readonly ILogger<VeloTimerAuthorizationMessageHandler> _logger;

        private readonly AuthenticationStateChangedHandler _authenticationStateChangedHandler;
        private AccessToken _lastToken;
        private AuthenticationHeaderValue _cachedHeader;
        private Uri[] _authorizedUris;
        private AccessTokenRequestOptions _tokenOptions;

        public VeloTimerAuthorizationMessageHandler(
            IAccessTokenProvider provider,
            AuthenticationStateProvider authStateProvider,
            NavigationManager navigation,
            IConfiguration config,
            ILogger<VeloTimerAuthorizationMessageHandler> logger)
        {
            _provider = provider;
            _authStateProvider = authStateProvider;
            _config = config;
            _navigation = navigation;
            _logger = logger;

            _authenticationStateChangedHandler = _ => { _lastToken = null; };
            _authStateProvider.AuthenticationStateChanged += _authenticationStateChangedHandler;

            ConfigureHandler(
                authorizedUrls: new[] { navigation.BaseUri }
                );
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Doing Things");
            var now = DateTimeOffset.Now;
            if (_authorizedUris == null)
            {
                throw new InvalidOperationException($"The '{nameof(AuthorizationMessageHandler)}' is not configured. " +
                    $"Call '{nameof(AuthorizationMessageHandler.ConfigureHandler)}' and provide a list of endpoint urls to attach the token to.");
            }

            var authState = await _authStateProvider.GetAuthenticationStateAsync();
            _logger.LogInformation($"Authentication User Identities: {authState.User?.Identity?.Name?.ToString() + " " + authState.User?.Identity?.IsAuthenticated.ToString()}");

            if (authState.User.Identity.IsAuthenticated && _authorizedUris.Any(uri => uri.IsBaseOf(request.RequestUri)))
            {
                if (_lastToken == null || now >= _lastToken.Expires.AddMinutes(-5))
                {
                    var tokenResult = _tokenOptions != null ?
                        await _provider.RequestAccessToken(_tokenOptions) :
                        await _provider.RequestAccessToken();

                    if (tokenResult.TryGetToken(out var token))
                    {
                        _lastToken = token;
                        _cachedHeader = new AuthenticationHeaderValue("Bearer", _lastToken.Value);
                    }
                    else
                    {
                        throw new AccessTokenNotAvailableException(_navigation, tokenResult, _tokenOptions?.Scopes);
                    }
                }

                // We don't try to handle 401s and retry the request with a new token automatically since that would mean we need to copy the request
                // headers and buffer the body and we expect that the user instead handles the 401s. (Also, we can't really handle all 401s as we might
                // not be able to provision a token without user interaction).
                request.Headers.Authorization = _cachedHeader;
            }

            return await base.SendAsync(request, cancellationToken);
        }

        public VeloTimerAuthorizationMessageHandler ConfigureHandler(
            IEnumerable<string> authorizedUrls,
            IEnumerable<string> scopes = null,
            string returnUrl = null)
        {
            if (_authorizedUris != null)
            {
                throw new InvalidOperationException("Handler already configured.");
            }

            if (authorizedUrls == null)
            {
                throw new ArgumentNullException(nameof(authorizedUrls));
            }

            var uris = authorizedUrls.Select(uri => new Uri(uri, UriKind.Absolute)).ToArray();
            if (uris.Length == 0)
            {
                throw new ArgumentException("At least one URL must be configured.", nameof(authorizedUrls));
            }

            _authorizedUris = uris;
            var scopesList = scopes?.ToArray();
            if (scopesList != null || returnUrl != null)
            {
                _tokenOptions = new AccessTokenRequestOptions
                {
                    Scopes = scopesList,
                    ReturnUrl = returnUrl
                };
            }

            return this;
        }

        void IDisposable.Dispose()
        {
            if (_provider is AuthenticationStateProvider authStateProvider)
            {
                authStateProvider.AuthenticationStateChanged -= _authenticationStateChangedHandler;
            }
            Dispose(disposing: true);
        }
    }
}
