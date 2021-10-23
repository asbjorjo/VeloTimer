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

            var passings = transponderId.HasValue ? _context.Passings.Where(p => transponderId.Value == p.TransponderId) : _context.Passings;

            var firstpass = await passings.OrderByDescending(p => p.Time)
                                          .Where(p => p.Loop == segment.Start)
                                          .Take(100)
                                          .LastOrDefaultAsync();

            if (firstpass == null)
            {
                return Enumerable.Empty<SegmentTimeRider>();
            }

            var loopIds = new List<long>
            {
                segment.StartId,
                segment.EndId
            };
            loopIds.AddRange(segment.Intermediates?.Select(i => i.LoopId));
            loopIds = loopIds.Distinct().ToList();

            var passingevents = passings.Where(p => loopIds.Contains(p.LoopId))
                                        .Where(p => p.Time > firstpass.Time)
                                        .Select(p => new
                                        {
                                            TransponderId = p.TransponderId,
                                            Rider = p.Transponder.Names.Where(n => n.ValidFrom <= p.Time || n.ValidUntil >= p.Time).SingleOrDefault(),
                                            Time = p.Time,
                                            LoopId = p.LoopId
                                        })
                                        .OrderBy(p => p.Time)
                                        .AsEnumerable()
                                        .GroupBy(p => p.TransponderId);

            var segmenttimes = new ConcurrentBag<SegmentTimeRider>();

            Parallel.ForEach(passingevents, transponderpassings =>
            {
                var transponderid = transponderpassings.Key;
                SegmentTimeRider segmenttimerider = new SegmentTimeRider
                {
                    Rider = transponderpassings.First().Rider?.Name ?? TransponderIdConverter.IdToCode(transponderid),
                    PassingTime = transponderpassings.First().Time,
                    Loop = segment.EndId,
                    Segmentlength = CalculateSegmentLength(segment.Start, segment.End)
                };

                foreach (var transponderpassing in transponderpassings.Skip(1))
                {
                    if (segment.Intermediates.Select(i => i.LoopId).Contains(transponderpassing.LoopId))
                    {
                        segmenttimerider.Intermediates.Add(new SegmentTime
                        {
                            Loop = transponderpassing.LoopId,
                            PassingTime = transponderpassing.Time,
                            Segmenttime = (transponderpassing.Time - segmenttimerider.PassingTime).TotalSeconds
                        });
                    }
                    if (transponderpassing.LoopId == segment.EndId)
                    {
                        segmenttimerider.Segmenttime = (transponderpassing.Time - segmenttimerider.PassingTime).TotalSeconds;
                        if (segmenttimerider.Segmenttime < segment.MaxTime && segmenttimerider.Segmenttime > segment.MinTime)
                        {
                            segmenttimes.Add(segmenttimerider);
                        }
                        segmenttimerider = new SegmentTimeRider
                        {
                            Rider = transponderpassing.Rider?.Name ?? TransponderIdConverter.IdToCode(transponderid),
                            Loop = segment.EndId,
                            Segmentlength = CalculateSegmentLength(segment.Start, segment.End)
                        };
                    }
                    if (transponderpassing.LoopId == segment.StartId)
                    {
                        segmenttimerider.PassingTime = transponderpassing.Time;
                        segmenttimerider.Intermediates.Clear();
                    }
                }
            });

            return segmenttimes.OrderByDescending(st => st.PassingTime);
        }

        private static double CalculateSegmentLength(TimingLoop startLoop, TimingLoop endLoop)
        {
            return startLoop.Distance < endLoop.Distance ?
                endLoop.Distance - startLoop.Distance :
                startLoop.Track.Length - startLoop.Distance + endLoop.Distance;
        }
    }
}
