using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using SlimMessageBus;
using VeloTime.Module.Facilities.Interface.Client;
using VeloTime.Module.Statistics.Interface.Messages;
using VeloTime.Module.Statistics.Model;
using VeloTime.Module.Statistics.Storage;

namespace VeloTime.Module.Statistics.Service;

internal class StatisticsService(StatisticsDbContext storage, IFacitiliesClient facilities, IMemoryCache cache, IMessageBus messageBus)
{

}
