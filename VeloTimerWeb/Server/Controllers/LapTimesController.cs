using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VeloTimerWeb.Server.Data;
using VeloTimer.Shared.Models;
using VeloTimer.Shared.Util;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

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
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<IEnumerable<LapTime>>> Get(long? startLoop, long? endLoop, long? transponderId, string? transponderLabel)
        {
            if (transponderId.HasValue 
                && transponderLabel != null
                && TransponderIdConverter.CodeToId(transponderLabel) != transponderId)
            {
                return BadRequest($"Transponder label {transponderLabel} and id {transponderId} do not correspond.");
            }

            if (!startLoop.HasValue)
            {
                startLoop = 1;
            }
            if (!endLoop.HasValue)
            {
                endLoop = 1;
            }

            var start = await _context.TimingLoops.Where(t => t.Id == startLoop).Include(t => t.Track).SingleAsync();
            var end = await _context.FindAsync<TimingLoop>(endLoop);

            double lapLength;

            if (start.Distance >= end.Distance)
            {
                lapLength = start.Track.Length - start.Distance + end.Distance;
            } 
            else
            {
                lapLength = end.Distance - start.Distance;
            }

            var dbPassings = _context.Set<Passing>().AsQueryable();
            
            if (transponderId.HasValue)
            {
                dbPassings = dbPassings.Where(p => p.TransponderId == transponderId);
            }

            var transponderPassings = await Task.Run(() =>
            {
                var passings = dbPassings
                                .OrderByDescending(p => p.Time)
                                .Where(p => p.LoopId == startLoop || p.LoopId == endLoop)
                                .Select(p => new
                                {
                                    TransponderId = p.TransponderId,
                                    Rider = p.Transponder.Name,
                                    Time = p.Time,
                                    LoopId = p.LoopId
                                })
                                .Take(1000)
                                .AsEnumerable();

                return passings.GroupBy(p => p.TransponderId);
            });

            var laptimes = new ConcurrentBag<LapTime>();

            Parallel.ForEach(transponderPassings, transponderPassing => {
                {
                    var transponder = transponderPassing.Key;
                    var endPassing = transponderPassing.First();

                    foreach (var passing in transponderPassing.Skip(1))
                    {
                        if (passing.LoopId == startLoop && endPassing.LoopId == endLoop)
                        {
                            laptimes.Add(new LapTime
                            {
                                Rider = TransponderIdConverter.IdToCode(passing.TransponderId),
                                PassingTime = endPassing.Time,
                                Laplength = lapLength,
                                Laptime = (endPassing.Time - passing.Time).TotalSeconds
                            });
                        }
                        endPassing = passing;
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
