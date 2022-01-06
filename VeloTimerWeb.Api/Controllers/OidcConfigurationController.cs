using Microsoft.AspNetCore.ApiAuthorization.IdentityServer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace VeloTimerWeb.Api.Controllers
{
    [Route("_configuration")]
    public class OidcConfigurationController : BaseController
    {
        public OidcConfigurationController(IClientRequestParametersProvider clientRequestParametersProvider, ILogger<OidcConfigurationController> logger) : base(logger)
        {
            ClientRequestParametersProvider = clientRequestParametersProvider;
        }

        public IClientRequestParametersProvider ClientRequestParametersProvider { get; }

        [HttpGet("{clientId}")]
        public IActionResult GetClientRequestParameters([FromRoute] string clientId)
        {
            var parameters = ClientRequestParametersProvider.GetClientParameters(HttpContext, clientId);
            return Ok(parameters);
        }
    }
}
