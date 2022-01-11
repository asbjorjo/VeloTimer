using AutoMapper;
using Azure.Messaging.ServiceBus;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Threading;
using System.Threading.Tasks;
using VeloTimer.Shared.Configuration;
using VeloTimer.Shared.Models.Timing;
using VeloTimerWeb.Api.Data;
using VeloTimerWeb.Api.Models.Timing;
using VeloTimerWeb.Api.Models.TrackSetup;

namespace VeloTimerWeb.Api.Services
{
    public class CreatePassingHandler : BackgroundService
    {
        private readonly MessageBusSettings _settings;
        private readonly ILogger<CreatePassingHandler> _logger;
        private readonly IMapper _mapper;
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private ServiceBusClient _client;
        private ServiceBusSessionProcessor _processor;

        public CreatePassingHandler(
            IOptions<MessageBusSettings> options,
            ILogger<CreatePassingHandler> logger,
            IMapper mapper,
            IServiceScopeFactory serviceScopeFactory)
        {
            _settings = options.Value;
            _logger = logger;
            _mapper = mapper;
            _serviceScopeFactory = serviceScopeFactory;
            _client = new ServiceBusClient(_settings.ConnectionString);
        }

        async Task PassingHandler(ProcessSessionMessageEventArgs args)
        {
            var passing = args.Message.Body.ToObjectFromJson<PassingRegister>();

            using var scope = _serviceScopeFactory.CreateScope();
            var passingService = scope.ServiceProvider.GetRequiredService<IPassingService>();
            var loopService = scope.ServiceProvider.GetRequiredService<VeloTimerDbContext>();

            var existing = await loopService.Set<Passing>().SingleOrDefaultAsync(x =>
                x.SourceId == passing.Source
                && x.Loop.LoopId == passing.LoopId
                && x.Time == passing.Time.UtcDateTime);

            if (existing != null)
            {
                await args.DeadLetterMessageAsync(args.Message);
                _logger.LogWarning($"Duplicate passing found {args.Message.MessageId} -- {existing.Id}");
                return;
            }

            var loop = await loopService.Set<TimingLoop>().SingleOrDefaultAsync(x => x.LoopId == passing.LoopId);

            if (loop == null)
            {
                await args.DeadLetterMessageAsync(args.Message);
                _logger.LogWarning($"Loop not found for {args.Message.MessageId} -- {passing.LoopId}");
                return;
            }

            var register = new Passing
            {
                SourceId = args.Message.MessageId,
                Time = passing.Time.UtcDateTime,
                Loop = loop,
            };

            await passingService.RegisterNew(register, passing.TimingSystem, passing.TransponderId);

            await args.CompleteMessageAsync(args.Message);
        }

        Task ErrorHandler(ProcessErrorEventArgs args)
        {
            _logger.LogError($"Source: {args.ErrorSource}");
            _logger.LogError($"Namespace: {args.FullyQualifiedNamespace}");
            _logger.LogError($"Entitypath: {args.EntityPath}");
            _logger.LogError($"Exception: {args.Exception}");

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

            await _processor.StartProcessingAsync();
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            if (_processor != null)
            {
                await _processor.StopProcessingAsync();
            }

            await base.StopAsync(cancellationToken);
        }
    }
}
