using AutoMapper;
using System.Linq;
using VeloTimer.Shared.Models.Riders;
using VeloTimer.Shared.Models.Statistics;
using VeloTimer.Shared.Models.Timing;
using VeloTimer.Shared.Models.TrackSetup;
using VeloTimer.Shared.Util;
using VeloTimerWeb.Api.Models.Riders;
using VeloTimerWeb.Api.Models.Statistics;
using VeloTimerWeb.Api.Models.Timing;
using VeloTimerWeb.Api.Models.TrackSetup;

namespace VeloTimerWeb.Api.Models
{
    public class VeloTimeProfile : Profile
    {
        public VeloTimeProfile()
        {
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
