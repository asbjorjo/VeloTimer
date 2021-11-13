using Microsoft.AspNetCore.Http;

namespace VeloTimerWeb.Api.Util.ApiAuthorization
{
    internal interface IAbsoluteUrlFactory
    {
        string GetAbsoluteUrl(string path);
        string GetAbsoluteUrl(HttpContext context, string path);
    }
}
