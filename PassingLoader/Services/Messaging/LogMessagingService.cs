using Microsoft.Extensions.Logging;
using VeloTimer.Shared.Data.Models.Timing;

namespace VeloTimer.PassingLoader.Services.Messaging
{
    internal class LogMessagingService : IMessagingService
    {
        private readonly ILogger _logger;

        public LogMessagingService(ILogger<LogMessagingService> logger) 
        {
            _logger = logger;
        }

        public async Task SubmitPassing(PassingRegister passing)
        {
            _logger.LogInformation(passing.ToString());

            await Task.CompletedTask;
        }

        public async Task SubmitPassings(IEnumerable<PassingRegister> passings)
        {
            foreach (PassingRegister passing in passings)
            {
                await SubmitPassing(passing);
            }
        }
    }
}
