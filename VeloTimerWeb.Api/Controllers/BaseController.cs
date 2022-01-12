using AutoMapper;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;

namespace VeloTimerWeb.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public abstract class BaseController : ControllerBase
    {
        protected readonly IMapper _mapper;
        protected readonly ILogger<BaseController> _logger;

        protected BaseController(ILogger<BaseController> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        protected BaseController(IMapper mapper, ILogger<BaseController> logger)
        {
            _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }
    }
}
