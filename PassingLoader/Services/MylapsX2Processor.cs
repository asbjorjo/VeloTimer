using Microsoft.Extensions.Hosting;
using VeloTimer.Shared.Services;

namespace PassingLoader.Services
{
    public class MylapsX2Processor : BackgroundService
    {
        private readonly IMylapsX2Service _x2Service;

        public MylapsX2Processor(IMylapsX2Service x2Service)
        {
            _x2Service = x2Service;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await Task.Yield();

            while (!stoppingToken.IsCancellationRequested)
            {
                _x2Service.ProcessQueue();
            }
        }
    }
}
