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

        public async Task<IEnumerable<SegmentTimeRider>> GetSegmentTimesAsync(long segmentId, long? transponderId)
        {
            var segment = await _context.Segments.Where(s => s.Id == segmentId)
                                                 .Include(s => s.Start)
                                                 .ThenInclude(t => t.Track)
                                                 .Include(s => s.End)
                                                 .Include(s => s.Intermediates)
                                                 .SingleOrDefaultAsync();
            
            if (segment == null)
            {
                throw new KeyNotFoundException();
            }

            double segmentLength = CalculateSegmentLength(segment.Start, segment.End);

            var loopIds = new List<long>
            {
                segment.StartId,
                segment.EndId
            };
            loopIds.AddRange(segment.Intermediates?.Select(i => i.LoopId));
            loopIds = loopIds.Distinct().ToList();

            var dbPassings = _context.Set<Passing>().AsQueryable();

            if (transponderId.HasValue)
            {
                dbPassings = dbPassings.Where(p => p.TransponderId == transponderId);
            }

            var passings = dbPassings
                                .Where(p => loopIds.Contains(p.LoopId))
                                .Select(p => new
                                {
                                    TransponderId = p.TransponderId,
                                    Rider = p.Transponder.Names.Where(n => n.ValidFrom <= p.Time || n.ValidUntil >= p.Time).SingleOrDefault(),
                                    Time = p.Time,
                                    LoopId = p.LoopId
                                })
                                .OrderByDescending(p => p.Time)
                                .Take(1000)
                                .AsEnumerable();

            var transponderPassings = passings.GroupBy(p => p.TransponderId);

            var segmenttimes = new ConcurrentBag<SegmentTimeRider>();

            Parallel.ForEach(transponderPassings, transponderPassing =>
            {
                {
                    var transponder = transponderPassing.Key;
                    var endPassing = transponderPassing.First();
                    List<SegmentTime> inters = new List<SegmentTime>();
                    
                    foreach (var passing in transponderPassing.Skip(1))
                    {
                        if (endPassing.LoopId != segment.EndId)
                        {
                            endPassing = passing;
                            inters = new List<SegmentTime>();
                        }
                        else if (passing.LoopId == segment.StartId)
                        {
                            segmenttimes.Add(new SegmentTimeRider
                            {
                                Rider = passing.Rider?.Name ?? TransponderIdConverter.IdToCode(passing.TransponderId),
                                PassingTime = endPassing.Time,
                                Segmentlength = segmentLength,
                                Segmenttime = (endPassing.Time - passing.Time).TotalSeconds,
                                Intermediates = inters
                            });
                            
                            inters = new List<SegmentTime>();
                            endPassing = passing;
                        }
                        else if (segment.Intermediates.Select(i => i.LoopId).Contains(passing.LoopId))
                        {
                            inters.Add(new SegmentTime
                            {
                                PassingTime = passing.Time,
                                Segmenttime = (endPassing.Time - passing.Time).TotalSeconds
                            });
                        }
                    }
                }
            });

            return segmenttimes.OrderByDescending(s => s.PassingTime);
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

            var passings = dbPassings
                                .OrderByDescending(p => p.Time)
                                .Where(p => p.Loop == startLoop || p.Loop == endLoop)
                                .Select(p => new
                                {
                                    TransponderId = p.TransponderId,
                                    Rider = p.Transponder.Names.Where(n => n.ValidFrom <= p.Time || n.ValidUntil >= p.Time).SingleOrDefault(),
                                    Time = p.Time,
                                    LoopId = p.LoopId
                                })
                                .Take(1000)
                                .AsEnumerable();

            var transponderPassings = passings.GroupBy(p => p.TransponderId);

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
                                Rider = passing.Rider?.Name ?? TransponderIdConverter.IdToCode(passing.TransponderId),
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
