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
    public class SegmentService : ISegmentService
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<SegmentService> _logger;

        public SegmentService(ApplicationDbContext context, ILogger<SegmentService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<IEnumerable<SegmentTimeRider>> GetSegmentTimesAsync(long segmentId, long? transponderId, DateTimeOffset? fromtime, TimeSpan? period)
        {
            var segment = await LoadSegment(segmentId);

            var passings = FilterPassings(_context.Passings, transponderId, fromtime, period);

            var firstpass = await FindFirstPassing(passings, segment);

            if (firstpass == null)
            {
                return Enumerable.Empty<SegmentTimeRider>();
            }

            var loopIds = GetSegmentLoopIds(segment);

            passings = GetPassings(passings, firstpass, loopIds);

            var passingevents = passings.Select(p => new
                                        {
                                            TransponderId = p.TransponderId,
                                            Rider = p.Transponder.Names.Where(n => n.ValidFrom <= p.Time && n.ValidUntil >= p.Time).SingleOrDefault(),
                                            Time = p.Time,
                                            Loop = p.Loop
                                        })
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
                    if (segment.Intermediates.Select(i => i.LoopId).Contains(transponderpassing.Loop.Id))
                    {
                        segmenttimerider.Intermediates.Add(new SegmentTime
                        {
                            Loop = transponderpassing.Loop.Id,
                            PassingTime = transponderpassing.Time,
                            Segmenttime = (transponderpassing.Time - segmenttimerider.PassingTime).TotalSeconds,
                            Segmentlength = CalculateSegmentLength(segment.Start, transponderpassing.Loop)
                        });
                    }
                    if (transponderpassing.Loop.Id == segment.EndId)
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
                    if (transponderpassing.Loop.Id == segment.StartId)
                    {
                        segmenttimerider.PassingTime = transponderpassing.Time;
                        segmenttimerider.Intermediates.Clear();
                    }
                }
            });

            return segmenttimes.OrderByDescending(st => st.PassingTime);
        }

        public async Task<IEnumerable<SegmentTimeRider>> GetFastestSegmentTime(long segmentId, long? transponderId, DateTimeOffset? fromtime, TimeSpan? period)
        {
            var times = await GetSegmentTimesAsync(segmentId, transponderId, fromtime, period);

            return times.OrderBy(st => st.Segmenttime);
        }

        public async Task<IEnumerable<KeyValuePair<string, long>>> GetSegmentPassingCountAsync(long segmentId, long? transponderId, DateTimeOffset? fromtime, TimeSpan? period)
        {
            var segment = await LoadSegment(segmentId);

            var passings = FilterPassings(_context.Passings, transponderId, fromtime, period);

            var firstpass = await FindFirstPassing(passings, segment);

            if (firstpass == null)
            {
                return Enumerable.Empty<KeyValuePair<string, long>>();
            }

            var loopIds = GetSegmentLoopIds(segment, false);

            passings = GetPassings(passings, firstpass, loopIds);

            var passingevents = passings.Select(p => new
                                        {
                                            TransponderId = p.TransponderId,
                                            Time = p.Time,
                                            LoopId = p.LoopId
                                        }).AsEnumerable().GroupBy(p => p.TransponderId);

            var segmentcounts = new ConcurrentDictionary<string, long>();

            Parallel.ForEach(passingevents, transponderpassings =>
            {
                long passingcount = 0;

                var transponderid = transponderpassings.Key;
                var starttime = transponderpassings.First().Time;

                foreach (var passing in transponderpassings.Skip(1))
                {
                    if (passing.LoopId == segment.EndId)
                    {
                        var time = (passing.Time - starttime).TotalSeconds;
                        if (time < segment.MaxTime && time > segment.MinTime)
                        {
                            passingcount++;
                        }

                    }
                    if (passing.LoopId == segment.StartId)
                    {
                        starttime = passing.Time;
                    }
                }

                segmentcounts.TryAdd(TransponderIdConverter.IdToCode(transponderid), passingcount);
            });

            return segmentcounts;
        }

        private async Task<Segment> LoadSegment(long segmentId)
        {
            var segment = await _context.Segments.Where(s => s.Id == segmentId)
                                                 .Include(s => s.Start).ThenInclude(t => t.Track)
                                                 .Include(s => s.End)
                                                 .Include(s => s.Intermediates)
                                                 .SingleOrDefaultAsync();

            if (segment == null)
            {
                throw new ArgumentException(
                    message: $"Specified segment - {segmentId} - does not exist.",
                    paramName: nameof(segmentId));
            }

            return segment;
        }

        private async Task<Passing> FindFirstPassing(IQueryable<Passing> passings, Segment segment)
        {
            return await passings.OrderBy(p => p.Time)
                                 .Where(p => p.Loop == segment.Start)
                                 .FirstOrDefaultAsync();
        }

        private static IQueryable<Passing> FilterPassings(IQueryable<Passing> passings, long? transponderId, DateTimeOffset? fromtime, TimeSpan? period)
        {
            passings = transponderId.HasValue ? passings.Where(p => transponderId.Value == p.TransponderId) : passings;
            passings = fromtime.HasValue ? passings.Where(p => p.Time >= fromtime) : passings;
            passings = period.HasValue ? passings.Where(p => p.Time <= fromtime + period) : passings;

            return passings;
        }

        private static IQueryable<Passing> GetPassings(IQueryable<Passing> passings, Passing firstpass, IEnumerable<long> loopIds)
        {
            return passings.Where(p => loopIds.Contains(p.LoopId))
                           .Where(p => p.Time >= firstpass.Time)
                           .OrderBy(p => p.Time);
        }

        private static IEnumerable<long> GetSegmentLoopIds(Segment segment, bool includeIntermediates = true)
        {
            var loopIds = new List<long>
            {
                segment.StartId,
                segment.EndId
            };

            if (includeIntermediates)
            {
                loopIds.AddRange(segment.Intermediates?.Select(i => i.LoopId));
            }

            return loopIds.Distinct();
        }

        private static double CalculateSegmentLength(TimingLoop startLoop, TimingLoop endLoop)
        {
            return startLoop.Distance < endLoop.Distance ?
                endLoop.Distance - startLoop.Distance :
                startLoop.Track.Length - startLoop.Distance + endLoop.Distance;
        }
    }
}
