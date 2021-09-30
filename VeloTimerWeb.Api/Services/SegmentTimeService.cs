using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VeloTimer.Shared.Models;
using VeloTimer.Shared.Util;
using VeloTimerWeb.Api.Data;

namespace VeloTimerWeb.Api.Services
{
    public class SegmentTimeService : ISegmentTimeService
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<SegmentTimeService> _logger;

        public SegmentTimeService(ApplicationDbContext context, ILogger<SegmentTimeService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<IEnumerable<LapTime>> GetSegmentTimesAsync(long start, long finish, long? transponder)
        {
            var startLoop = await _context.TimingLoops
                .Where(t => t.Id == start)
                .Include(t => t.Track)
                .SingleAsync();
            var endLoop = startLoop;

            if (finish != start)
            {
                endLoop = await _context.FindAsync<TimingLoop>(finish);
            }

            double segmentLength = CalculateSegmentLength(startLoop, endLoop);

            var dbPassings = _context.Set<Passing>().AsQueryable();

            if (transponder.HasValue)
            {
                dbPassings = dbPassings.Where(p => p.TransponderId == transponder);
            }

            var transponderPassings = await Task.Run(() =>
            {
                var passings = dbPassings
                                .OrderByDescending(p => p.Time)
                                .Where(p => p.Loop == startLoop || p.Loop == endLoop)
                                .Select(p => new
                                {
                                    TransponderId = p.TransponderId,
                                    Rider = p.Transponder.Label,
                                    Time = p.Time,
                                    LoopId = p.LoopId
                                })
                                .Take(1000)
                                .AsEnumerable();

                return passings.GroupBy(p => p.TransponderId);
            });

            var laptimes = new ConcurrentBag<LapTime>();

            Parallel.ForEach(transponderPassings, transponderPassing =>
            {
                {
                    var transponder = transponderPassing.Key;
                    var endPassing = transponderPassing.First();

                    foreach (var passing in transponderPassing.Skip(1))
                    {
                        if (passing.LoopId == startLoop.Id && endPassing.LoopId == endLoop.Id)
                        {
                            laptimes.Add(new LapTime
                            {
                                Rider = TransponderIdConverter.IdToCode(passing.TransponderId),
                                PassingTime = endPassing.Time,
                                Laplength = segmentLength,
                                Laptime = (endPassing.Time - passing.Time).TotalSeconds
                            });
                        }
                        endPassing = passing;
                    }
                }
            });

            return laptimes.OrderByDescending(l => l.PassingTime);
        }

        private static double CalculateSegmentLength(TimingLoop startLoop, TimingLoop endLoop)
        {
            return startLoop.Distance < endLoop.Distance ?
                endLoop.Distance - startLoop.Distance :
                startLoop.Track.Length - startLoop.Distance + endLoop.Distance;
        }
    }
}
