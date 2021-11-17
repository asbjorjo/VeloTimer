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

        public async Task<IEnumerable<SegmentTimeRider>> GetSegmentTimesNew(long SegmentId, DateTimeOffset? FromTime, DateTimeOffset? ToTime, int Count)
        {
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

            var times = Enumerable.Empty<SegmentTimeRider>();

            var query = from sr in _context.Set<SegmentRun>()
                        join to in _context.Set<TransponderOwnership>() on sr.Start.TransponderId equals to.TransponderId
                        join r in _context.Set<Rider>() on to.OwnerId equals r.Id
                        where sr.SegmentId == SegmentId
                              && sr.Start.Time >= fromtime && sr.End.Time <= totime
                              && to.OwnedFrom <= sr.Start.Time && sr.End.Time < to.OwnedUntil
                              && sr.End.Transponder.Passings.Where(p => p.Time > sr.Start.Time && p.Time < sr.End.Time && sr.Segment.Intermediates.Select(i => i.LoopId).Contains(p.LoopId)).Count() == sr.Segment.Intermediates.Count()
                        orderby sr.End.Time descending
                        select new SegmentTimeRider
                        {
                            Loop = sr.End.LoopId,
                            PassingTime = sr.End.Time,
                            Rider = r.Name,
                            Segmentlength = CalculateSegmentLength(sr.Segment.Start.Distance, sr.Segment.End.Distance, sr.Segment.Start.Track.Length),
                            Segmenttime = sr.Time,
                            Intermediates = sr.End.Transponder.Passings
                                .Where(p => p.Time > sr.Start.Time && p.Time < sr.End.Time && sr.Segment.Intermediates.Select(i => i.LoopId).Contains(p.LoopId))
                                .Select(i => new SegmentTime
                                {
                                    Loop = i.LoopId,
                                    PassingTime = i.Time,
                                    Segmentlength = CalculateSegmentLength(sr.Segment.Start.Distance, sr.Segment.End.Distance, sr.Segment.Start.Track.Length),
                                    Segmenttime = (i.Time - sr.Start.Time).TotalSeconds
                                })
                                .ToList()
                        };

            times = await query
                .Take(Count)
                .AsNoTrackingWithIdentityResolution()
                .ToListAsync();

            return times;
        }

        public async Task<IEnumerable<SegmentTimeRider>> GetSegmentTimes(long segmentId, long? transponderId, DateTimeOffset? fromtime, DateTimeOffset? totime)
        {
            var segment = await LoadSegment(segmentId);

            var passings = FilterPassings(_context.Passings, transponderId, fromtime, totime);

            var firstpass = await FindFirstPassing(passings, segment);

            if (firstpass == null)
            {
                return Enumerable.Empty<SegmentTimeRider>();
            }

            var loopIds = GetSegmentLoopIds(segment);

            passings = GetPassings(passings, firstpass, loopIds);

            var passingevents = passings
                .Select(p => new
                {
                    p.TransponderId,
                    Rider = p.Transponder.Owners.Where(n => n.OwnedFrom <= p.Time && n.OwnedUntil >= p.Time).Select(to => to.Owner).SingleOrDefault(),
                    p.Time,
                    p.Loop,
                    p.Transponder
                })
                .AsNoTrackingWithIdentityResolution()
                .AsEnumerable()
                .GroupBy(p => p.TransponderId);

            var segmenttimes = new ConcurrentBag<SegmentTimeRider>();

            Parallel.ForEach(passingevents, transponderpassings =>
            {
                var transponderid = transponderpassings.Key;
                SegmentTimeRider segmenttimerider = new()
                {
                    Rider = transponderpassings.First().Rider?.Name ?? TransponderIdConverter.IdToCode(long.Parse(transponderpassings.First().Transponder.SystemId)),
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
                            if (segment.RequireIntermediates && segmenttimerider.Intermediates.Count() == segment.Intermediates.Count())
                            {
                                segmenttimes.Add(segmenttimerider);
                            }
                            else if (!segment.RequireIntermediates && segmenttimerider.Intermediates.Count() <= segment.Intermediates.Count())
                            {
                                segmenttimes.Add(segmenttimerider);
                            }
                            
                        }
                        segmenttimerider = new SegmentTimeRider
                        {
                            Rider = transponderpassing.Rider?.Name ?? TransponderIdConverter.IdToCode(long.Parse(transponderpassing.Transponder.SystemId)),
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

        public async Task<IEnumerable<SegmentTimeRider>> GetFastestSegmentTimes(long segmentId, long? transponderId, DateTimeOffset? fromtime, DateTimeOffset? totime, int? count, bool requireintermediates)
        {
            var times = await GetSegmentTimes(segmentId, transponderId, fromtime, totime);

            if (requireintermediates)
            {
                var segment = await LoadSegment(segmentId);
                times = times.Where(st => st.Intermediates.Count() == segment.Intermediates.Count());
            }

            times = times.OrderBy(st => st.Segmenttime);

            if (count.HasValue)
            {
                times = times.Take(count.Value);
            }

            return times;
        }

        public async Task<IEnumerable<KeyValuePair<string, long>>> GetSegmentPassingCount(long segmentId, long? transponderId, DateTimeOffset? fromtime, DateTimeOffset? totime, int? count)
        {
            var owners = from to in _context.Set<TransponderOwnership>()
                            join r in _context.Set<Rider>() on to.OwnerId equals r.Id
                         from sr in _context.Set<SegmentRun>()
                         where 
                            sr.SegmentId == segmentId
                            && sr.Time >= sr.Segment.MinTime && sr.Time <= sr.Segment.MaxTime 
                            && to.TransponderId == sr.End.TransponderId 
                            && sr.End.Time >= fromtime && sr.End.Time >= to.OwnedFrom && sr.End.Time < to.OwnedUntil 
                            && to.OwnerId == r.Id
                            && sr.Segment.Intermediates.Count == sr.End.Transponder.Passings.Where(p => p.Time > sr.Start.Time && p.Time < sr.End.Time && sr.Segment.Intermediates.Select(i => i.LoopId).Contains(p.LoopId)).Count()
                         group sr by new { to.OwnerId, r.Name } into o
                         orderby o.LongCount() descending
                         select new 
                         { 
                             Rider = o.Key.Name, 
                             Count = o.LongCount() 
                         };

            if (count.HasValue)
            {
                owners = owners.Take(count.Value);
            }

            var runs = await owners
                .AsNoTrackingWithIdentityResolution()
                .ToDictionaryAsync(k => k.Rider, v => v.Count);

            return runs;
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

        private static async Task<Passing> FindFirstPassing(IQueryable<Passing> passings, Segment segment)
        {
            return await passings
                .OrderBy(p => p.Time)
                .Where(p => p.Loop == segment.Start)
                .FirstOrDefaultAsync();
        }

        private static IQueryable<Passing> FilterPassings(IQueryable<Passing> passings, long? transponderId, DateTimeOffset? fromtime, DateTimeOffset? totime)
        {
            passings = transponderId.HasValue ? passings.Where(p => transponderId.Value == p.TransponderId) : passings;
            passings = fromtime.HasValue ? passings.Where(p => p.Time >= fromtime) : passings;
            passings = totime.HasValue ? passings.Where(p => p.Time <= totime) : passings;

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

        private static double CalculateSegmentLength(double start, double end, double track)
        {
            return start < end ?
                end - start :
                track - start + end;
        }

        public async Task<IEnumerable<SegmentTimeRider>> GetFastestSegmentTimesNewWay(long SegmentId, DateTimeOffset? FromTime, DateTimeOffset? ToTime, int Count, bool RequireIntermediates)
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

            var query = from sr in _context.Set<SegmentRun>()
                            join to in _context.Set<TransponderOwnership>() on sr.Start.TransponderId equals to.TransponderId
                                join r in _context.Set<Rider>() on to.OwnerId equals r.Id
                        where sr.SegmentId == SegmentId
                              && sr.Start.Time >= fromtime && sr.End.Time <= totime
                              && to.OwnedFrom <= sr.Start.Time && sr.End.Time < to.OwnedUntil
                              && sr.End.Transponder.Passings.Where(p => p.Time > sr.Start.Time && p.Time < sr.End.Time && sr.Segment.Intermediates.Select(i => i.LoopId).Contains(p.LoopId)).Count() == sr.Segment.Intermediates.Count()
                        orderby sr.Time ascending
                        select new SegmentTimeRider
                        {
                            Loop = sr.End.LoopId,
                            PassingTime = sr.End.Time,
                            Rider = r.Name,
                            Segmentlength = CalculateSegmentLength(sr.Segment.Start.Distance, sr.Segment.End.Distance, sr.Segment.Start.Track.Length),
                            Segmenttime = sr.Time,
                            Intermediates = sr.End.Transponder.Passings
                                .Where(p => p.Time > sr.Start.Time && p.Time < sr.End.Time && sr.Segment.Intermediates.Select(i => i.LoopId).Contains(p.LoopId))
                                .Select(i => new SegmentTime
                                {
                                    Loop = i.LoopId,
                                    PassingTime = i.Time,
                                    Segmentlength = CalculateSegmentLength(sr.Segment.Start.Distance, sr.Segment.End.Distance, sr.Segment.Start.Track.Length),
                                    Segmenttime = (i.Time - sr.Start.Time).TotalSeconds
                                })
                                .ToList()
                        };

            times = await query
                .Take(Count)
                .AsNoTrackingWithIdentityResolution()
                .ToListAsync();

            return times;
        }
    }
}
