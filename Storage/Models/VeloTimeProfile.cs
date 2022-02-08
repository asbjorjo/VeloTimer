using AutoMapper;
using VeloTime.Storage.Models.Riders;
using VeloTime.Storage.Models.Statistics;
using VeloTime.Storage.Models.Timing;
using VeloTime.Storage.Models.TrackSetup;
using VeloTimer.Shared.Data.Models.Riders;
using VeloTimer.Shared.Data.Models.Statistics;
using VeloTimer.Shared.Data.Models.Timing;
using VeloTimer.Shared.Data.Models.TrackSetup;
using VeloTimer.Shared.Util;

namespace VeloTime.Storage.Models
{
    public class VeloTimeProfile : Profile
    {
        public VeloTimeProfile()
        {
            CreateMap<Activity, ActivityWeb>()
                .ForMember(dest => dest.Start, opt => opt.MapFrom(src => src.Sessions.OrderBy(x => x.Start).First().Start))
                .ForMember(dest => dest.End, opt => opt.MapFrom(src => src.Sessions.OrderByDescending(x => x.End).First().End));
            CreateMap<Session, SessionWeb>();
            CreateMap<Passing, PassingWeb>()
                .ForMember(dest => dest.Track, opt => opt.MapFrom(src => src.Loop.Track));
            CreateMap<Rider, RiderWeb>()
                .ForMember(dest => dest.DisplayName, opt => opt.MapFrom(src => src.Name))
                .ReverseMap();
            CreateMap<StatisticsItem, StatisticsItemWeb>();
            CreateMap<TimingLoop, TimingLoopWeb>();
            CreateMap<Track, TrackWeb>();
            CreateMap<TrackLayout, TrackLayoutWeb>()
                .ForMember(dest => dest.Length, opt => opt.MapFrom(src => src.Distance));
            CreateMap<TrackSegment, TrackSegmentWeb>();
            CreateMap<TrackStatisticsItem, TrackStatisticsItemWeb>();
            CreateMap<Transponder, TransponderWeb>()
                .ForMember(dest => dest.Label, opt => opt.MapFrom(src => TransponderIdConverter.IdToCode(src.SystemId)))
                .ForMember(dest => dest.LastSeen, opt => opt.MapFrom(src => src.Passings.OrderByDescending(x => x.Time).FirstOrDefault()));
            CreateMap<TransponderOwnership, TransponderOwnershipWeb>();
        }
    }
}
