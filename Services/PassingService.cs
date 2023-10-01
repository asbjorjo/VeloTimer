using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VeloTime.Storage.Data;
using VeloTime.Storage.Models.Statistics;
using VeloTime.Storage.Models.Timing;
using VeloTime.Storage.Models.TrackSetup;
using VeloTimer.Shared.Hub;
using static VeloTimer.Shared.Data.Models.Timing.TransponderType;

namespace VeloTime.Services
{
    public class PassingService : IPassingService
    {
        private readonly VeloTimerDbContext _context;
        private readonly ILogger<PassingService> _logger;

        public PassingService(
            VeloTimerDbContext context,
            ILogger<PassingService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<bool> Exists(Passing passing)
        {
            return await Existing(passing) != null;
        }

        public async Task<Passing?> Existing(Passing passing)
        {
            if (passing == null) throw new ArgumentNullException(nameof(passing));
            if (passing.Time == default) throw new ArgumentNullException(nameof(passing), "Time");
            if (passing.Transponder == null) throw new ArgumentNullException(nameof(passing), "Transponder");
            if (passing.Loop == null) throw new ArgumentNullException(nameof(passing), "Loop");

            var existing = await _context.Set<Passing>()
                                         .SingleOrDefaultAsync(x => x.Transponder == passing.Transponder
                                                                   && x.Loop == passing.Loop
                                                                   && x.Time == passing.Time);

            return existing;
        }

        public async Task<Passing> RegisterNew(Passing passing, TimingSystem? timingSystem, string transponderId)
        {
            if (timingSystem.HasValue && transponderId != null)
            {
                var transponder = await _context.Set<Transponder>()
                    .Where(t => t.TimingSystem == timingSystem && t.SystemId == transponderId)
                    .SingleOrDefaultAsync();

                if (transponder == null)
                {
                    transponder = new Transponder
                    {
                        SystemId = transponderId,
                        TimingSystem = timingSystem.Value
                    };

                    _context.Add(transponder);
                }

                passing.Transponder = transponder;
            }

            return await RegisterNew(passing);
        }

        public async Task<Passing> RegisterNew(Passing passing)
        {
            if (await Exists(passing))
            {
                _logger.LogWarning("Tried to register existing -- {Track} - {Loop} - {Time} - {Transponder}", passing.Loop.Track.Slug, passing.Loop.LoopId, passing.Time, passing.Transponder.Id);
                return passing;
            }

            using var transaction = _context.Database.BeginTransaction();

            try
            {
                int changes;

                _context.Add(passing);
                changes = await _context.SaveChangesAsync();
                _logger.LogInformation("New passing -- {Track} - {Loop}/{LoopDescr} - {Time} - {Transponder} - {Changes}", passing.Loop.Track.Slug, passing.Loop.LoopId, passing.Loop.Description, passing.Time, passing.Transponder.Id, changes);

                var transponderPassing = await RegisterTransponderPassing(passing);

                if (transponderPassing != null)
                {
                    _logger.LogInformation("New transponderpassing -- {Time} - {Transponder}", transponderPassing.EndTime, transponderPassing.Transponder.Id);
                    var sectorPassings = await RegisterTrackSectorPassings(transponderPassing);

                    if (sectorPassings.Any())
                    {
                        changes = await _context.SaveChangesAsync();
                        _logger.LogInformation("New sectorpassings -- {Transponder} - {Count} - {Changes}", sectorPassings.First().Transponder.Id, sectorPassings.Count(), changes);

                        var layoutpassings = Enumerable.Empty<TrackLayoutPassing>();

                        foreach (var sectorPassing in sectorPassings)
                        {
                            var lp = await RegisterLayoutPassings(sectorPassing);
                            layoutpassings = layoutpassings.Concat(lp);
                        }

                        if (layoutpassings.Any())
                        {
                            changes = await _context.SaveChangesAsync();
                            _logger.LogInformation("New layoutpassings -- {Transponder} - {Count} - {Changes}", layoutpassings.First().Transponder.Id, layoutpassings.Count(), changes);

                            var transponderstats = Enumerable.Empty<TransponderStatisticsItem>();
                            foreach (var layoutpassing in layoutpassings)
                            {
                                var tsi = await RegisterStatistics(layoutpassing);
                                transponderstats = transponderstats.Concat(tsi);
                            }

                            if (transponderstats.Any())
                            {
                                await _context.SaveChangesAsync();
                                _logger.LogInformation("New transponderstats -- {Transponder} - {Count}", transponderstats.First().Transponder.Id, transponderstats.Count());
                            }
                        }
                    }
                }

                changes = await _context.SaveChangesAsync();
                _logger.LogInformation($"Changes: {changes}");

                transaction.Commit();
            }
            catch(Exception ex)
            {
                _logger.LogError("Unable to save passing.", ex);

                transaction.Rollback();

                throw;
            }

            return passing;
        }

        private async Task<IEnumerable<TransponderStatisticsItem>> RegisterStatistics(TrackLayoutPassing layoutPassing)
        {
            var transponderstats = Enumerable.Empty<TransponderStatisticsItem>();

            var statsitems = await _context.Set<TrackStatisticsItem>()
                .Where(x => x.Layout == layoutPassing.TrackLayout)
                .OrderByDescending(x => x.Laps)
                .ToListAsync();

            if (statsitems.Any())
            {
                var layoutPassings = await _context.Set<TrackLayoutPassing>()
                    .Where(x =>
                        x.TrackLayout == layoutPassing.TrackLayout
                        && x.Transponder == layoutPassing.Transponder
                        && x.EndTime <= layoutPassing.EndTime)
                    .OrderByDescending(x => x.EndTime)
                    .Take(statsitems.First().Laps)
                    .OrderBy(x => x.EndTime)
                    .ToListAsync();

                foreach (var item in statsitems)
                {
                    if (item.Laps <= layoutPassings.Count)
                    {
                        var laps = layoutPassings.TakeLast(item.Laps);
                        bool continuouslap = true;

                        if (item.Laps > 1)
                        {
                            var previouslappass = laps.First();
                            foreach (var lap in laps.Skip(1))
                            {
                                if (previouslappass.EndTime != lap.StartTime)
                                {
                                    continuouslap = false;
                                }
                                previouslappass = lap;
                            }
                        }

                        if (continuouslap)
                        {
                            var tsi = TransponderStatisticsItem.Create(item, layoutPassing.Transponder, laps);
                            _context.Add(tsi);
                        }
                    }
                }
            }

            return transponderstats;
        }

        private async Task<IEnumerable<TrackLayoutPassing>> RegisterLayoutPassings(TrackSectorPassing sectorPassing)
        {
            var passings = Enumerable.Empty<TrackLayoutPassing>();

            var layouts = await _context.Set<TrackLayout>()
                .Where(x => x.Active == true)
                .Where(x => x.Sectors.OrderByDescending(x => x.Order).First().Sector == sectorPassing.TrackSector)
                .Include(x => x.Sectors)
                .ThenInclude(x => x.Sector)
                .ToListAsync();

            _logger.LogInformation("Found {Layouts} ending with {Sector}", layouts.Count, sectorPassing.TrackSector.Id);

            if (layouts.Any())
            {
                foreach (var layout in layouts)
                {
                    _logger.LogInformation("Processing {Layout}", layout.Slug);

                    var transponderpassings = await _context.Set<TrackSectorPassing>()
                        .Where(x => x.Transponder == sectorPassing.Transponder)
                        .Where(x => x.EndTime <= sectorPassing.EndTime)
                        .Where(x => layout.Sectors.Select(x => x.Sector).Contains(x.TrackSector))
                        .OrderByDescending(x => x.EndTime)
                        .Take(layout.Sectors.Count)
                        .OrderBy(x => x.EndTime)
                        .ToListAsync();

                    var sectors = layout.Sectors.OrderBy(x => x.Order).Select(x => x.Sector);
                    _logger.LogInformation("Checking {Sectors} sectors using {Passings} passings", sectors.Count(), transponderpassings.Count);

                    _logger.LogInformation("Layout sectors: {Sectors}", string.Join(",", sectors.Select(x => x.Id)));
                    _logger.LogInformation("Passing sectors: {Sectors}", string.Join(",", transponderpassings.Select(x => x.TrackSector.Id)));

                    var continuous = transponderpassings.Select(x => x.TrackSector.Id).SequenceEqual(sectors.Select(x => x.Id));
                    _logger.LogInformation("Continuous? {Continuous}", continuous);

                    var previous = transponderpassings.First();
                    foreach (var transponderpassing in transponderpassings.Skip(1))
                    {
                        if (previous.EndTime != transponderpassing.StartTime)
                        {
                            continuous = false;
                        }
                        previous = transponderpassing;
                    }

                    if (continuous)
                    {
                        var passing = TrackLayoutPassing.Create(layout, sectorPassing.Transponder, transponderpassings);
                        _context.Add(passing);
                        passings = passings.Append(passing);
                    }

                    _logger.LogInformation("Found {Passings} continous layouts", passings.Count());
                }
            }

            return passings;
        }

        private async Task<IEnumerable<TrackSectorPassing>> RegisterTrackSectorPassings(TrackSegmentPassing transponderPassing)
        {
            var passings = Enumerable.Empty<TrackSectorPassing>();
            var trackSectors = await _context.Set<TrackSector>()
                .Where(x => x.Segments.OrderByDescending(x => x.Order).First().Segment == transponderPassing.TrackSegment)
                .Include(x => x.Segments)
                .ThenInclude(x => x.Segment)
            .ToListAsync();

            _logger.LogInformation("Found {Sectors} ending with {Segment}", trackSectors.Count, transponderPassing.TrackSegment.Id);

            if (trackSectors.Any())
            {
                var segmentpassings = await _context.Set<TrackSegmentPassing>()
                    .Where(x => x.Transponder == transponderPassing.Transponder)
                    .Where(x => x.EndTime <= transponderPassing.StartTime)
                    .OrderByDescending(x => x.EndTime)
                    .Take(trackSectors.Max(x => x.Segments.Count))
                    .OrderBy(x => x.EndTime)
                    .Include(x => x.TrackSegment)
                    .ToListAsync();

                segmentpassings.Add(transponderPassing);

                foreach (var sector in trackSectors)
                {
                    var relevant = segmentpassings.TakeLast(sector.Segments.Count);
                    var sectorPassing = TrackSectorPassing.Create(sector, transponderPassing.Transponder, relevant);
                    if (sectorPassing != null)
                    {
                        passings = passings.Append(sectorPassing);
                        _context.Add(sectorPassing);
                    }
                }
            }

            _logger.LogInformation("Produced {Passings} passings", passings.Count());
            return passings;
        }

        public async Task<TrackSegmentPassing?> RegisterTransponderPassing(Passing passing)
        {
            var trackSegments = await _context.Set<TrackSegment>()
                .Include(s => s.Start)
                .Include(s => s.End)
                .Where(s => s.Active == true && s.End == passing.Loop)
                .ToListAsync();

            _logger.LogInformation("Found {Segments} ending with {Loop}/{LoopDescr} at {Track}", trackSegments.Count, passing.Loop.Id, passing.Loop.Description, passing.Loop.Track.Slug);

            foreach (var trackSegment in trackSegments)
            {
                var previous = await _context.Set<Passing>()
                    .Where(p => p.Transponder == passing.Transponder)
                    .Where(p => p.Time < passing.Time)
                    .OrderByDescending(p => p.Time)
                    .Include(s => s.Loop)
                    .FirstOrDefaultAsync();


                if (previous != null) _logger.LogInformation("Found previous passing {Passing} past {Loop}/{LoopDescr} at {Time}", previous.Id, previous.Loop.Id, previous.Loop.Description, previous.Time);

                if (previous != null && previous.Loop == trackSegment.Start)
                {
                    var transponderPassing = TrackSegmentPassing.Create(trackSegment, previous, passing);
                    _context.Add(transponderPassing);
                    await _context.SaveChangesAsync();
                    return transponderPassing;
                }
            }

            _logger.LogInformation("Found nothing, returning nothing");
            return null;
        }
    }
}
