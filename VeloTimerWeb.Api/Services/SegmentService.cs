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
        private readonly VeloTimerDbContext _context;
        private readonly ILogger<SegmentService> _logger;

        public SegmentService(VeloTimerDbContext context, ILogger<SegmentService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<IEnumerable<SegmentTimeRider>> GetSegmentTimes(long SegmentId, DateTimeOffset? FromTime, DateTimeOffset? ToTime, int Count)
        {
            var times = Enumerable.Empty<SegmentTimeRider>();

            var fromtime = DateTimeOffset.MinValue;
            var totime = DateTimeOffset.MaxValue;

            if (FromTime.HasValue)
            {
                fromtime = FromTime.Value;
            }
            if (ToTime.HasValue)
            {
                totime = ToTime.Value;
            }
            if (totime <= fromtime || Count <= 0)
            {
                return times;
            }

            var segment = await LoadSegment(SegmentId);

            var query = from sr in _context.Set<SegmentRun>()
                        join to in _context.Set<TransponderOwnership>() on sr.Start.TransponderId equals to.TransponderId
                        join r in _context.Set<Rider>() on to.OwnerId equals r.Id
                        where sr.SegmentId == SegmentId
                              && sr.Start.Time >= fromtime && sr.End.Time <= totime
                              && sr.Time >= segment.MinTime && sr.Time <= segment.MaxTime
                              && to.OwnedFrom <= sr.Start.Time && sr.End.Time < to.OwnedUntil
                              && sr.End.Transponder.Passings.Where(
                                  p => p.Time > sr.Start.Time && p.Time < sr.End.Time 
                                    && segment.Intermediates.Select(i => i.LoopId).Contains(p.LoopId)).Count() == segment.Intermediates.Count()
                        orderby sr.End.Time descending
                        select new SegmentTimeRider
                        {
                            Loop = sr.End.LoopId,
                            PassingTime = sr.End.Time,
                            Rider = r.Name,
                            Segmentlength = CalculateSegmentLength(segment.Start.Distance, segment.End.Distance, segment.Start.Track.Length),
                            Segmenttime = sr.Time,
                            Intermediates = sr.End.Transponder.Passings
                                .Where(p => p.Time > sr.Start.Time && p.Time < sr.End.Time && segment.Intermediates.Select(i => i.LoopId).Contains(p.LoopId))
                                .Select(i => new SegmentTime
                                {
                                    Loop = i.LoopId,
                                    PassingTime = i.Time,
                                    Segmentlength = CalculateSegmentLength(segment.Start.Distance, i.Loop.Distance, segment.Start.Track.Length),
                                    Segmenttime = (i.Time - sr.Start.Time).TotalSeconds
                                })
                        };

            query = query.Take(Count);

            _logger.LogDebug(query.ToQueryString());

            times = await query
                .AsNoTracking()
                .ToListAsync();

            return times;
        }

        public async Task<IEnumerable<SegmentTimeRider>> GetSegmentTimesForRider(long SegmentId, DateTimeOffset? FromTime, DateTimeOffset? ToTime, long RiderId, int Count = 50)
        {
            var times = Enumerable.Empty<SegmentTimeRider>();

            var fromtime = DateTimeOffset.MinValue;
            var totime = DateTimeOffset.MaxValue;

            if (FromTime.HasValue)
            {
                fromtime = FromTime.Value;
            }
            if (ToTime.HasValue)
            {
                totime = ToTime.Value;
            }
            if (totime <= fromtime || Count <= 0)
            {
                return times;
            }

            var segment = await LoadSegment(SegmentId);

            var query = from sr in _context.Set<SegmentRun>()
                        join to in _context.Set<TransponderOwnership>() on sr.Start.TransponderId equals to.TransponderId
                        join r in _context.Set<Rider>() on to.OwnerId equals r.Id
                        where sr.SegmentId == SegmentId
                              && to.OwnerId == RiderId
                              && sr.Start.Time >= fromtime && sr.End.Time <= totime
                              && sr.Time >= segment.MinTime && sr.Time <= segment.MaxTime
                              && to.OwnedFrom <= sr.Start.Time && sr.End.Time < to.OwnedUntil
                              && sr.End.Transponder.Passings.Where(
                                  p => p.Time > sr.Start.Time && p.Time < sr.End.Time
                                    && segment.Intermediates.Select(i => i.LoopId).Contains(p.LoopId)).Count() == segment.Intermediates.Count()
                        orderby sr.End.Time descending
                        select new SegmentTimeRider
                        {
                            Loop = sr.End.LoopId,
                            PassingTime = sr.End.Time,
                            Rider = r.Name,
                            Segmentlength = CalculateSegmentLength(segment.Start.Distance, segment.End.Distance, segment.Start.Track.Length),
                            Segmenttime = sr.Time,
                            Intermediates = sr.End.Transponder.Passings
                                .Where(p => p.Time > sr.Start.Time && p.Time < sr.End.Time && segment.Intermediates.Select(i => i.LoopId).Contains(p.LoopId))
                                .Select(i => new SegmentTime
                                {
                                    Loop = i.LoopId,
                                    PassingTime = i.Time,
                                    Segmentlength = CalculateSegmentLength(segment.Start.Distance, i.Loop.Distance, segment.Start.Track.Length),
                                    Segmenttime = (i.Time - sr.Start.Time).TotalSeconds
                                })
                        };

            query = query.Take(Count);

            _logger.LogDebug(query.ToQueryString());

            times = await query
                .AsNoTracking()
                .ToListAsync();

            return times;
        }

        public async Task<IEnumerable<KeyValuePair<string, int>>> GetSegmentPassingCount(long SegmentId, DateTimeOffset? FromTime, DateTimeOffset? ToTime, int Count)
        {
            var runs = new Dictionary<string, int>();
            var fromtime = DateTimeOffset.MinValue;
            var totime = DateTimeOffset.MaxValue;

            if (FromTime.HasValue)
            {
                fromtime = FromTime.Value;
            }
            if (ToTime.HasValue)
            {
                totime = ToTime.Value;
            }
            if (totime <= fromtime || Count <= 0)
            {
                return runs;
            }

            var segment = await LoadSegment(SegmentId);

            var owners = from sr in _context.Set<SegmentRun>()
                            join to in _context.Set<TransponderOwnership>() on sr.Start.TransponderId equals to.TransponderId
                                join r in _context.Set<Rider>() on to.OwnerId equals r.Id
                         let intermediatecount = (from p in _context.Set<Passing>()
                                                  where sr.Start.Time < p.Time && sr.End.Time > p.Time
                                                     && p.TransponderId == to.TransponderId
                                                     && segment.Intermediates.Select(i => i.LoopId).Contains(p.LoopId)
                                                  select p.Id).Count()
                         where
                            sr.SegmentId == SegmentId
                            && sr.End.Time >= fromtime && sr.End.Time >= to.OwnedFrom && sr.End.Time < to.OwnedUntil
                            && sr.Time >= segment.MinTime && sr.Time <= segment.MaxTime 
                            //&& segment.Intermediates.Count == sr.End.Transponder.Passings.Where(p => p.Time > sr.Start.Time && p.Time < sr.End.Time && segment.Intermediates.Select(i => i.LoopId).Contains(p.LoopId)).Count()
                         group sr by new { to.OwnerId, r.Name } into o
                         orderby o.Count() descending
                         select new 
                         { 
                             Rider = o.Key.Name, 
                             Count = o.Count() 
                         };

            owners = owners.Take(Count);

            _logger.LogDebug(owners.ToQueryString());

            runs = await owners
                .AsNoTracking()
                .ToDictionaryAsync(k => k.Rider, v => v.Count);

            return runs;
        }

        public async Task<IEnumerable<SegmentTimeRider>> GetFastestSegmentTimes(long SegmentId, DateTimeOffset? FromTime, DateTimeOffset? ToTime, int Count)
        {
            var times = Enumerable.Empty<SegmentTimeRider>();
            var fromtime = DateTimeOffset.MinValue;
            var totime = DateTimeOffset.MaxValue;

            if (FromTime.HasValue)
            {
                fromtime = FromTime.Value;
            }
            if (ToTime.HasValue)
            {
                totime = ToTime.Value;
            }

            if (totime <= fromtime || Count <= 0)
            {
                return times;
            }

            var segment = await LoadSegment(SegmentId);

            var query = from sr in _context.Set<SegmentRun>()
                            join to in _context.Set<TransponderOwnership>() on sr.Start.TransponderId equals to.TransponderId
                                join r in _context.Set<Rider>() on to.OwnerId equals r.Id
                        let intermediatecount = (from p in _context.Set<Passing>() 
                                                 where sr.Start.Time < p.Time && sr.End.Time > p.Time 
                                                    && p.TransponderId == to.TransponderId 
                                                    && segment.Intermediates.Select(i => i.LoopId).Contains(p.LoopId)
                                                 select p.Id).Count()
                        where sr.SegmentId == SegmentId
                              && sr.Start.Time >= fromtime && sr.End.Time <= totime
                              && sr.Time >= segment.MinTime && sr.Time <= segment.MaxTime
                              && to.OwnedFrom <= sr.Start.Time && sr.End.Time < to.OwnedUntil
                              && intermediatecount == segment.Intermediates.Count()
                        orderby sr.Time ascending
                        select new SegmentTimeRider
                        {
                            Loop = sr.End.LoopId,
                            PassingTime = sr.End.Time,
                            Rider = r.Name,
                            Segmenttime = sr.Time,
                            Segmentlength = CalculateSegmentLength(segment.Start, segment.End)
                        };

            query = query.Take(Count);

            _logger.LogDebug(query.ToQueryString());

            times = await query
                .AsNoTracking()
                .ToListAsync();

            var segmentLength = CalculateSegmentLength(segment.Start.Distance, segment.End.Distance, segment.Start.Track.Length);

            foreach (var t in times)
            {
                t.Segmentlength = segmentLength;

                foreach (var i in t.Intermediates)
                {
                    i.Segmentlength = CalculateSegmentLength(segment.Start.Distance, i.Segmentlength, segment.Start.Track.Length);
                }
            }

            return times;
        }

        public async Task<IEnumerable<SegmentTimeRider>> GetFastestSegmentTimesForRider(long SegmentId, DateTimeOffset? FromTime, DateTimeOffset? ToTime, long RiderId, int Count = 10)
        {
            var times = Enumerable.Empty<SegmentTimeRider>();
            var fromtime = DateTimeOffset.MinValue;
            var totime = DateTimeOffset.MaxValue;

            if (FromTime.HasValue)
            {
                fromtime = FromTime.Value;
            }
            if (ToTime.HasValue)
            {
                totime = ToTime.Value;
            }

            if (totime <= fromtime || Count <= 0)
            {
                return times;
            }

            var segment = await LoadSegment(SegmentId);

            var query = from sr in _context.Set<SegmentRun>()
                        join to in _context.Set<TransponderOwnership>() on sr.Start.TransponderId equals to.TransponderId
                        join r in _context.Set<Rider>() on to.OwnerId equals r.Id
                        where sr.SegmentId == SegmentId
                              && to.OwnerId == RiderId
                              && sr.Start.Time >= fromtime && sr.End.Time <= totime
                              && sr.Time >= segment.MinTime && sr.Time <= segment.MaxTime
                              && to.OwnedFrom <= sr.Start.Time && sr.End.Time < to.OwnedUntil
                              && sr.End.Transponder.Passings.Where(
                                  p => p.Time > sr.Start.Time && p.Time < sr.End.Time
                                    && segment.Intermediates.Select(i => i.LoopId).Contains(p.LoopId)).Count() == segment.Intermediates.Count()
                        orderby sr.Time ascending
                        select new SegmentTimeRider
                        {
                            Loop = sr.End.LoopId,
                            PassingTime = sr.End.Time,
                            Rider = r.Name,
                            Segmenttime = sr.Time,
                            Intermediates = sr.End.Transponder.Passings
                                .Where(p => p.Time > sr.Start.Time && p.Time < sr.End.Time && segment.Intermediates.Select(i => i.LoopId).Contains(p.LoopId))
                                .Select(i => new SegmentTime
                                {
                                    Loop = i.LoopId,
                                    PassingTime = i.Time,
                                    Segmentlength = i.Loop.Distance,
                                    Segmenttime = (i.Time - sr.Start.Time).TotalSeconds
                                })
                        };

            query = query.Take(Count);

            _logger.LogDebug(query.ToQueryString());

            times = await query
                .AsNoTracking()
                .ToListAsync();

            var segmentLength = CalculateSegmentLength(segment.Start.Distance, segment.End.Distance, segment.Start.Track.Length);

            foreach (var t in times)
            {
                t.Segmentlength = segmentLength;

                foreach (var i in t.Intermediates)
                {
                    i.Segmentlength = CalculateSegmentLength(segment.Start.Distance, i.Segmentlength, segment.Start.Track.Length);
                }
            }

            return times;
        }

        private async Task<Segment> LoadSegment(long segmentId)
        {
            var segment = await _context.Segments
                .AsNoTrackingWithIdentityResolution()
                .Where(s => s.Id == segmentId)
                .Include(s => s.Start).ThenInclude(t => t.Track)
                .Include(s => s.End)
                .Include(s => s.Intermediates).ThenInclude(i => i.Loop)
                .SingleOrDefaultAsync();

            if (segment == null)
            {
                throw new ArgumentException(
                    message: $"Specified segment - {segmentId} - does not exist.",
                    paramName: nameof(segmentId));
            }

            return segment;
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

        private static double CalculateSegmentLength(double start, double end, double track)
        {
            return start < end ?
                end - start :
                track - start + end;
        }
    }
}
