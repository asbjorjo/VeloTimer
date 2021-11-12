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

        public async Task<IEnumerable<SegmentTimeRider>> GetSegmentTimesAsync(long segmentId, long? transponderId, DateTimeOffset? fromtime, DateTimeOffset? totime)
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

            var passingevents = passings.Select(p => new
                                        {
                                            p.TransponderId,
                                            Rider = p.Transponder.Owners.Where(n => n.OwnedFrom <= p.Time && n.OwnedUntil >= p.Time).Select(to => to.Owner).SingleOrDefault(),
                                            p.Time,
                                            p.Loop,
                                            p.Transponder
                                        })
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
            var times = await GetSegmentTimesAsync(segmentId, transponderId, fromtime, totime);

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

        public async Task<IEnumerable<KeyValuePair<string, long>>> GetSegmentPassingCountAsync(long segmentId, long? transponderId, DateTimeOffset? fromtime, DateTimeOffset? totime)
        {
            var segment = await LoadSegment(segmentId);

            var passings = FilterPassings(_context.Passings, transponderId, fromtime, totime);

            var firstpass = await FindFirstPassing(passings, segment);

            if (firstpass == null)
            {
                return Enumerable.Empty<KeyValuePair<string, long>>();
            }

            var loopIds = GetSegmentLoopIds(segment, false);

            passings = GetPassings(passings, firstpass, loopIds);

            var passingevents = passings.Select(p => new
                                        {
                                            p.TransponderId,
                                            p.Time,
                                            p.LoopId
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
            return await passings.OrderBy(p => p.Time)
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

        public async Task<IEnumerable<SegmentTimeRider>> GetFastestSegmentTimesNewWay(long segmentId, long? transponderId, DateTimeOffset? fromtime, DateTimeOffset? totime, int? count, bool requireintermediates)
        {
            var segmentruns = _context.Set<SegmentRun>().Where(s => s.SegmentId == segmentId);
            if (fromtime.HasValue)
            {
                segmentruns = segmentruns.Where(s => s.Start.Time >= fromtime.Value);
            }
            if (totime.HasValue)
            {
                segmentruns = segmentruns.Where(s => s.End.Time <= totime.Value);
            }
            if (transponderId.HasValue)
            {
                segmentruns = segmentruns.Where(s => s.Start.TransponderId == transponderId.Value);
            }

            if (requireintermediates)
            {
                //segmentruns = segmentruns.Where(s => s.IntermediateCount == s.Segment.Intermediates.Count);
                segmentruns = segmentruns.Where(s => s.Segment.Intermediates.Count == s.End.Transponder.Passings.Where(p => s.Segment.Intermediates.Select(i => i.Loop).Contains(p.Loop) && p.Time > s.Start.Time && p.Time < s.End.Time).Count());
            }

            segmentruns = segmentruns.Where(s => s.Time >= s.Segment.MinTime && s.Time <= s.Segment.MaxTime);
            segmentruns = segmentruns
                .Include(s => s.Segment)
                .ThenInclude(s => s.Start)
                .ThenInclude(s => s.Track)
                .Include(s => s.Segment)
                .ThenInclude(s => s.End)
                .Include(s => s.Segment)
                .ThenInclude(s => s.Intermediates)
                .ThenInclude(s => s.Loop);

            segmentruns = segmentruns.OrderBy(s => s.Time);

            var runquery = segmentruns
                .Select(s => new
                {
                    Time = s.Time,
                    Rider = s.End.Transponder.Owners.Where(o => o.OwnedFrom <= s.Start.Time && o.OwnedUntil >= s.End.Time).Select(o => o.Owner).SingleOrDefault(),
                    Transponder = s.Start.Transponder,
                    Intermediates = s.End.Transponder.Passings.Where(p => s.Segment.Intermediates.Select(i => i.Loop).Contains(p.Loop) && p.Time > s.Start.Time && p.Time < s.End.Time).DefaultIfEmpty(),
                    End = s.End,
                    Segment = s.Segment
                });

            if (count.HasValue)
            {
                runquery = runquery.Take(count.Value);
            }   

            var runs = await runquery.ToListAsync();

            var times = runs
                .Select(w => new SegmentTimeRider
                {
                    Rider = w.Rider?.Name ?? TransponderIdConverter.IdToCode(long.Parse(w.Transponder.SystemId)),
                    Loop = w.End.LoopId,
                    PassingTime = w.End.Time,
                    Segmentlength = CalculateSegmentLength(w.Segment.Start, w.Segment.End),
                    Segmenttime = w.Time,
                    Intermediates = w.Intermediates?.Select(i => new SegmentTime { Loop = i.LoopId, PassingTime = i.Time, Segmentlength = CalculateSegmentLength(w.Segment.Start, i.Loop), Segmenttime = (w.End.Time - i.Time).TotalSeconds }).ToList()
                }).ToList();

            return times;
        }
    }
}
