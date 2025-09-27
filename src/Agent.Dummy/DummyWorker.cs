using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VeloTime.Agent.Dummy
{
    internal class DummyWorker : IHostedService
    {
        private ILogger<DummyWorker> logger;

        public DummyWorker(ILogger<DummyWorker> logger)
        {
            this.logger = logger;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            logger.LogInformation("Dummy Worker started");
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            logger.LogInformation("Dummy Worker stopped");
            return Task.CompletedTask;
        }
    }
}
