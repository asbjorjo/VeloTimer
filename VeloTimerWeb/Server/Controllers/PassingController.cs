using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using System.Threading.Tasks;
using VeloTimerWeb.Server.Models.Mylaps;
using VeloTimerWeb.Server.Services.Mylaps;

// For more information on enabling Web API for empty projects, visit https://go.microsoft.com/fwlink/?LinkID=397860

namespace VeloTimerWeb.Server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class PassingController : ControllerBase
    {
        private readonly AmmcPassingService _passingService;

        public PassingController(AmmcPassingService passingService)
        {
            _passingService = passingService;
        }

        // GET: api/<PassingController>
        [HttpGet]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<ActionResult<IEnumerable<Passing>>> Get()
        {
            return await _passingService.GetAll();
        }

        // GET api/<PassingController>/5
        [HttpGet("{id:length(24)}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<Passing>> Get(string id)
        {
            var passing = await _passingService.Get(id);

            if (passing == null)
            {
                return NotFound();
            }

            return passing;
        }

        // POST api/<PassingController>
        [HttpPost]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public IActionResult Post([FromBody] Passing passing)
        {
            return BadRequest();
        }

        // PUT api/<PassingController>/5
        [HttpPut("{id}")]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public IActionResult Put(string id, [FromBody] Passing passing)
        {
            return BadRequest();
        }

        // DELETE api/<PassingController>/5
        [HttpDelete("{id}")]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public IActionResult Delete(string id)
        {
            return BadRequest();
        }
    }
}
