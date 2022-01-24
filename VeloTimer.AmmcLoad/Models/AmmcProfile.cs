using AutoMapper;
using VeloTimer.Shared.Data.Models.Timing;

namespace VeloTimer.AmmcLoad.Models
{
    public class AmmcProfile : Profile
    {
        public AmmcProfile()
        {
            CreateMap<PassingAmmc, PassingRegister>()
                .ForMember(dest => dest.Source, opt => opt.MapFrom(src => src.Id))
                .ForMember(dest => dest.Time, opt => opt.MapFrom(src => src.UtcTime))
                .ForMember(dest => dest.TimingSystem, opt => opt.MapFrom(src => TransponderType.TimingSystem.Mylaps_X2));
        }
    }
}
