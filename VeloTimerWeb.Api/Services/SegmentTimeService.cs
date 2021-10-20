using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
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
                throw new ArgumentException(
                    message: $"Specified segment - {segmentId} - does not exist.", 
                    paramName: nameof(segmentId));
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
                    var segmenttimerider = new SegmentTimeRider();
                    
                    foreach (var passing in transponderPassing.Skip(1))
                    {
                        if (endPassing.LoopId != segment.EndId)
                        {
                            endPassing = passing;
                            segmenttimerider = new SegmentTimeRider();
                        }
                        else if (passing.LoopId == segment.StartId)
                        {
                            segmenttimerider.Rider = endPassing.Rider?.Name ?? TransponderIdConverter.IdToCode(transponder);
                            segmenttimerider.PassingTime = endPassing.Time;
                            segmenttimerider.Segmentlength = segmentLength;
                            segmenttimerider.Segmenttime = (endPassing.Time - passing.Time).TotalSeconds;
                            segmenttimerider.Loop = endPassing.LoopId;

                            if (segmenttimerider.Intermediates.Count > segment.Intermediates.Count)
                            {
                                _logger.LogWarning($"Too many intermediate times found:" +
                                    $" - {TransponderIdConverter.IdToCode(transponder)}" +
                                    $" - {segment.Label}" +
                                    $" - {passing.Time}" +
                                    $" - {endPassing.Time}" +
                                    $" - {segmenttimerider.Segmenttime}" +
                                    $" - {segmenttimerider.Intermediates.Count}");
                                segmenttimerider.Intermediates.Clear();
                            } else {
                                foreach (var inter in segmenttimerider.Intermediates)
                                {
                                    inter.Segmenttime = (inter.PassingTime - passing.Time).TotalSeconds;
                                }
                            }

                            segmenttimes.Add(segmenttimerider);

                            endPassing = passing;
                            segmenttimerider = new SegmentTimeRider();
                        }
                        else if (segment.Intermediates.Select(i => i.LoopId).Contains(passing.LoopId))
                        {
                            segmenttimerider.Intermediates.Add(new SegmentTime
                            {
                                PassingTime = passing.Time,
                                Loop = passing.LoopId
                            });
                        }
                    }
                }
            });

            return segmenttimes.OrderByDescending(s => s.PassingTime);
        }

        private static double CalculateSegmentLength(TimingLoop startLoop, TimingLoop endLoop)
        {
            return startLoop.Distance < endLoop.Distance ?
                endLoop.Distance - startLoop.Distance :
                startLoop.Track.Length - startLoop.Distance + endLoop.Distance;
        }
    }
}
