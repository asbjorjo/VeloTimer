using AutoMapper;
using System.Linq;
using VeloTimer.Shared.Models;
using VeloTimer.Shared.Util;

namespace VeloTimerWeb.Api.Models
{
    public class VeloTimeProfile : Profile
    {
        public VeloTimeProfile()
        {
            CreateMap<Passing, PassingWeb>()
                .ForMember(dest => dest.Track, opt => opt.MapFrom(src => src.Loop.Track));
            CreateMap<Rider, RiderWeb>()
                .ForMember(dest => dest.DisplayName, opt => opt.MapFrom(src => src.Name));
            CreateMap<StatisticsItem, StatisticsItemWeb>();
            CreateMap<TimingLoop, TimingLoopWeb>();
            CreateMap<Track, TrackWeb>();
            CreateMap<TrackLayout, TrackLayoutWeb>();
            CreateMap<TrackSegment, TrackSegmentWeb>();
            CreateMap<TrackStatisticsItem, TrackStatisticsItemWeb>();
            CreateMap<Transponder, TransponderWeb>()
                .ForMember(dest => dest.Label, opt => opt.MapFrom(src => TransponderIdConverter.IdToCode(src.SystemId)))
                .ForMember(dest => dest.LastSeen, opt => opt.MapFrom(src => src.Passings.OrderByDescending(x => x.Time).FirstOrDefault()));
            CreateMap<TransponderOwnership, TransponderOwnershipWeb>();
        }
    }
}
