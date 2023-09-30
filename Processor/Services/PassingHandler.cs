using Azure.Messaging.ServiceBus;
using VeloTime.Services;
using VeloTime.Shared.Messaging;
using VeloTime.Storage.Models.Timing;
using VeloTimer.Shared.Data.Models.Timing;

namespace VeloTime.Processor.Services
{
    public class PassingHandler : BackgroundService
    {
        private readonly MessageBusOptions _settings;
        private readonly ILogger<PassingHandler> _logger;
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly ServiceBusClient _client;
        private readonly ServiceBusSessionProcessor _processor;

        public PassingHandler(
            MessageBusOptions options,
            ILogger<PassingHandler> logger,
            IServiceScopeFactory serviceScopeFactory)
        {
            _settings = options;
            _logger = logger;
            _serviceScopeFactory = serviceScopeFactory;
            _client = new ServiceBusClient(_settings.ConnectionString);


            var processoptions = new ServiceBusSessionProcessorOptions
            {
                AutoCompleteMessages = false,
                MaxConcurrentSessions = _settings.MaxConcurrency,
            };

            _processor = _client.CreateSessionProcessor(_settings.QueueName, "incoming", processoptions);
        }

        async Task HandlePassing(ProcessSessionMessageEventArgs args)
        {
            var passing = args.Message.Body.ToObjectFromJson<PassingRegister>();

            if (string.IsNullOrWhiteSpace(passing.Track)) passing.Track = "sola-arena";

            using var scope = _serviceScopeFactory.CreateScope();
            var passingService = scope.ServiceProvider.GetRequiredService<IPassingService>();
            var transponderService = scope.ServiceProvider.GetRequiredService<ITransponderService>();
            var trackService = scope.ServiceProvider.GetRequiredService<ITrackService>();
            var statService = scope.ServiceProvider.GetRequiredService<IStatisticsService>();

            var transponder = await transponderService.FindOrRegister(passing.TimingSystem, passing.TransponderId);

            if (transponder == null)
            {
                await AbandonPassing(args, $"Transponder not found or registered - {passing.TimingSystem} -- {passing.TransponderId}");
                return;
            }

            var track = await trackService.GetTrackBySlug(passing.Track);
            if (track == null)
            {
                await AbandonPassing(args, $"Track not configured - {passing.Track}");
                return;
            }

            var loop = track.TimingLoops.SingleOrDefault(x => x.LoopId == passing.LoopId);
            if (loop == null)
            {
                await AbandonPassing(args, $"Loop not configured - {passing.Track} - {passing.LoopId}");
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
                _logger.LogDebug("{MessageId} - {SourceId}", register.SourceId, existing.SourceId);
                await DeadLetterPassing(args, $"Duplicate -- {passing.Track} - {passing.Time} - {passing.TransponderId} - {passing.LoopId}");
                return;
            }

            var newPassing = await passingService.RegisterNew(register);

            if (newPassing != null)
            {
                await statService.CreateOrUpdateActivity(newPassing);
            }

            await args.CompleteMessageAsync(args.Message);
        }

        private async Task DeadLetterPassing(ProcessSessionMessageEventArgs args, string message)
        {
            _logger.LogWarning("Passing not processed: {Reason}", message);
            await args.DeadLetterMessageAsync(args.Message, message);
        }

        private async Task AbandonPassing(ProcessSessionMessageEventArgs args, string message)
        {
            _logger.LogWarning("Passing not processed: {Reason}", message);
            await args.AbandonMessageAsync(args.Message);
        }

        Task ErrorHandler(ProcessErrorEventArgs args)
        {
            _logger.LogError("{Source} -- {Namespace} - {EntityPath} - {Exception} - {ExSource} - {ExMessage}",
                             args.ErrorSource,
                             args.FullyQualifiedNamespace,
                             args.EntityPath,
                             args.Exception.GetType(),
                             args.Exception.Source,
                             args.Exception.Message);

            return Task.CompletedTask;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _processor.ProcessMessageAsync += HandlePassing;
            _processor.ProcessErrorAsync += ErrorHandler;

            _logger.LogInformation("Start processing");
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
