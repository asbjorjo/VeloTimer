using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VeloTimerWeb.Server.Data;
using VeloTimerWeb.Shared.Models;

namespace VeloTimerWeb.Server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class LapTimesController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<LapTimesController> _logger;

        public LapTimesController(ApplicationDbContext applicationDbContext, ILogger<LapTimesController> logger)
        {
            _context = applicationDbContext;
            _logger = logger;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<LapTime>>> Get()
        {
            var transponderPassings = await Task.Run(() =>
            {
                var passings = _context.Set<Passing>()
                                                   .OrderByDescending(p => p.Time)
                                                   .Where(p => p.Loop.LoopId == 0)
                                                   .Select(p => new
                                                   {
                                                       TransponderId = p.TransponderId,
                                                       Rider = p.Transponder.Name,
                                                       Time = p.Time
                                                   })
                                                   .Take(1000)
                                                   .AsEnumerable();
                return passings.GroupBy(p => p.TransponderId);
            });

            var laptimes = new ConcurrentBag<LapTime>();

            Parallel.ForEach(transponderPassings, transponderPassing => {
                {
                    var transponder = transponderPassing.Key;
                    DateTime previous = DateTime.MinValue;

                    foreach (var passing in transponderPassing)
                    {
                        if (previous > DateTime.MinValue)
                        {
                            laptimes.Add(new LapTime
                            {
                                Rider = transponder.ToString(),
                                PassingTime = previous,
                                Laplength = 250,
                                Laptime = (previous - passing.Time).TotalSeconds
                            });
                        }
                        previous = passing.Time;
                    }
                }
            });

            return laptimes.OrderByDescending(l => l.PassingTime).ToList();
        }
    }

    public class PassingDetail
    {
        public long TransponderId { get; set; }
        public string Rider { get; set; }
        public DateTime Time { get; set; }
    }
}
