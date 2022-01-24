using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VeloTimer.Shared.Data.Models.Timing;
using VeloTimer.Shared.Services.Messaging;
using VeloTimerWeb.Api.Models.Timing;

namespace VeloTimerWeb.Api.Services
{
    public class CreatePassingHandler : BackgroundService
    {
        private readonly MessageBusOptions _settings;
        private readonly ILogger<CreatePassingHandler> _logger;
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly ServiceBusClient _client;
        private ServiceBusSessionProcessor _processor;

        public CreatePassingHandler(
            MessageBusOptions options,
            ILogger<CreatePassingHandler> logger,
            IServiceScopeFactory serviceScopeFactory)
        {
            _settings = options;
            _logger = logger;
            _serviceScopeFactory = serviceScopeFactory;
            _client = new ServiceBusClient(_settings.ConnectionString);
        }

        async Task PassingHandler(ProcessSessionMessageEventArgs args)
        {
            var passing = args.Message.Body.ToObjectFromJson<PassingRegister>();

            if (string.IsNullOrWhiteSpace(passing.Track)) passing.Track = "sola-arena";

            using var scope = _serviceScopeFactory.CreateScope();
            var passingService = scope.ServiceProvider.GetRequiredService<IPassingService>();
            var transponderService = scope.ServiceProvider.GetRequiredService<ITransponderService>();
            var trackService = scope.ServiceProvider.GetRequiredService<ITrackService>();

            var transponder = await transponderService.FindOrRegister(passing.TimingSystem, passing.TransponderId);

            if (transponder == null)
            {
                await DeadLetterPassing(args, $"Transponder not found or registered - {passing.TimingSystem} -- {passing.TransponderId}");
                return;
            }

            var track = await trackService.GetTrackBySlug(passing.Track);
            if (track == null)
            {
                await DeadLetterPassing(args, $"Track not configured - {passing.Track}");
                return;
            }            

            var loop = track.TimingLoops.SingleOrDefault(x => x.LoopId == passing.LoopId);
            if (loop == null)
            {
                await DeadLetterPassing(args, $"Loop not configured - {passing.Track} - {passing.LoopId}");
                return;
            }

            var register = new Passing
            {
                SourceId = args.Message.MessageId,
                Time = passing.Time.UtcDateTime,
                Loop = loop,
                Transponder = transponder
            };

            var existing = await passingService.Existing(register);
            if (existing != null)
            {
                await DeadLetterPassing(args, $"Duplicate -- {passing.Track} - {passing.Time} - {passing.TransponderId} - {passing.LoopId}");
                return;
            }

            await passingService.RegisterNew(register, passing.TimingSystem, passing.TransponderId);

            await args.CompleteMessageAsync(args.Message);
        }

        private async Task DeadLetterPassing(ProcessSessionMessageEventArgs args, string message)
        {
            _logger.LogWarning("Passing not processed: {Reason}", message);
            await args.DeadLetterMessageAsync(args.Message);
        }

        Task ErrorHandler(ProcessErrorEventArgs args)
        {
            _logger.LogError("{Source} -- {Namespace} - {EntityPath}",
                             args.ErrorSource,
                             args.FullyQualifiedNamespace,
                             args.EntityPath);

            return Task.CompletedTask;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var options = new ServiceBusSessionProcessorOptions
            {
                AutoCompleteMessages = false,
                MaxConcurrentSessions = 10,
            };

            _processor = _client.CreateSessionProcessor(_settings.QueueName, options);

            _processor.ProcessMessageAsync += PassingHandler;
            _processor.ProcessErrorAsync += ErrorHandler;

            await _processor.StartProcessingAsync(stoppingToken);
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            if (_processor != null)
            {
                await _processor.StopProcessingAsync(cancellationToken);
            }

            await base.StopAsync(cancellationToken);
        }
    }
}
