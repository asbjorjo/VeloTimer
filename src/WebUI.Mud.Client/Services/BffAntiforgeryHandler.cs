namespace VeloTime.WebUI.Mud.Client.Services;

public sealed class BffAntiforgeryHandler(Uri baseAddress) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.RequestUri is not { IsAbsoluteUri: true } uri ||
            uri.Scheme != baseAddress.Scheme || uri.Authority != baseAddress.Authority)
        {
            throw new InvalidOperationException("BFF requests must use the WebUI origin.");
        }

        if (request.Method != HttpMethod.Get && request.Method != HttpMethod.Head && request.Method != HttpMethod.Options)
        {
            using var tokenRequest = new HttpRequestMessage(HttpMethod.Get, new Uri(baseAddress, "api/antiforgery"));
            using var tokenResponse = await base.SendAsync(tokenRequest, cancellationToken);
            tokenResponse.EnsureSuccessStatusCode();
            var token = await tokenResponse.Content.ReadFromJsonAsync<string>(cancellationToken)
                ?? throw new InvalidOperationException("The antiforgery token is missing.");
            request.Headers.Add("X-CSRF-TOKEN", token);
        }

        return await base.SendAsync(request, cancellationToken);
    }
}