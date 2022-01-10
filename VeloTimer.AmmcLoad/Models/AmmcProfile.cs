﻿using AutoMapper;
using VeloTimer.Shared.Models.Timing;

namespace VeloTimer.AmmcLoad.Models
{
    public class AmmcProfile : Profile
    {
        public AmmcProfile()
        {
            CreateMap<PassingAmmc, PassingRegister>()
                .ForMember(dest => dest.Source, opt => opt.MapFrom(src => src.Id))
                .ForMember(dest => dest.TimingSystem, opt => opt.MapFrom(src => TransponderType.TimingSystem.Mylaps_X2));
        }
    }
}
