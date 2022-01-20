using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MylapsSDK.MylapsSDKLibrary;
using MylapsSDK.Objects;
using PassingLoader.Configuration;
using VeloTimer.Shared.Data.Models.Timing;
using VeloTimer.Shared.Services;

namespace PassingLoader.Services
{
    public class MylapsX2Service : IMylapsX2Service, IDisposable
    {
        private readonly ILogger<MylapsX2Service> _logger;
        private readonly IMessagingService _messagingService;
        private readonly MylapsX2Options _settings;

        private readonly SDK _sdk;
        private readonly MTA _mta;

        public MylapsX2Service(
            IOptions<MylapsX2Options> options,
            IMessagingService messaging,
            ILogger<MylapsX2Service> logger)
        {
            _settings = options.Value;
            _logger = logger;
            _messagingService = messaging;
            _sdk = SDK.CreateSDK("VeloTime.X2");
            _mta = _sdk.CreateMTA();
        }

        private void RegisterPassingEvent(List<Passing> passings)
        {
            foreach (var passing in passings)
            {
                _logger.LogInformation("passing - {ID} - {UTCtime} - {TransponderID} - {LoopID} - {Hits} - {LowHitsWarning} - {Quality} - {LowStrengthWarning} - {LowBattery} -  {Resend} - {DeviceResend}",
                                       passing.ID,
                                       passing.UTCTimeAsDateTime,
                                       passing.TransponderID,
                                       passing.LoopID,
                                       passing.Hits,
                                       passing.HasLowHitsWarning(),
                                       passing.GetQuality(),
                                       passing.HasLowStrengthWarning(),
                                       passing.IsLowBattery(),
                                       passing.IsResend(),
                                       passing.IsDeviceResend());

                PassingRegister register = new()
                {
                    Track = "sola-arena",
                    LoopId = passing.LoopID,
                    Time = new DateTimeOffset(passing.UTCTimeAsDateTime),
                    TransponderId = passing.TransponderID.ToString(),
                    TimingSystem = TransponderType.TimingSystem.Mylaps_X2,
                    Source = passing.ID.ToString(),
                };

                _messagingService.SubmitPassing(register);
            }
        }

        private void HandlePassing(MDP_NOTIFY_TYPE type, List<Passing> passings, EventData subject)
        {
            _logger.LogInformation("passing - {Type} - {Count}", type, passings.Count);
            switch (type)
            {
                case MDP_NOTIFY_TYPE.MDP_NOTIFY_INSERT:
                    RegisterPassingEvent(passings);
                    break;
                case MDP_NOTIFY_TYPE.MDP_NOTIFY_SELECT:
                    RegisterPassingEvent(passings);
                    break;
            }
        }

        private void HandlePassingTrigger(MDP_NOTIFY_TYPE type, List<PassingTrigger> triggers, EventData subject)
        {
            _logger.LogInformation("trigger - {Type} - {Count}", type, triggers.Count);
            foreach (var trigger in triggers)
            {
                _logger.LogInformation("trigger - {ID} - {UTCtime} - {TransponderID} - {LoopID} - {Type}",
                    trigger.ID, trigger.UTCTimeAsDateTime, trigger.TransponderID, trigger.LoopID, trigger.Type);
            }
        }

        private void HandleConnect(bool isConnected, MTA subject)
        {
            var state = isConnected ? "connected" : "disconnected";
            _logger.LogInformation("Stream {State}", state);
        }

        private void HandleConnectionStateChanged(CONNECTIONSTATE state, MTA subject)
        {
            _logger.LogInformation("Connection state changed {State}", state);
            switch (state)
            {
                case CONNECTIONSTATE.csConnectFailed:
                    //var timer = new Timer(Reconnect, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
                    break;
            }
        }

        public void ProcessQueue()
        {
            _sdk.ProcessMessageQueue(TimeSpan.FromTicks(1));
        }

        private void Connect(DateTime start)
        {
            _mta.NotifyConnectHandlers += HandleConnect;
            _mta.NotifyConnectionStateHandlers += HandleConnectionStateChanged;
            _mta.Connect(_settings.Hostname, _settings.Username, _settings.Password);

            var eventData = _mta.CreateEventDataLiveWithResend(start.ToUniversalTime());
            eventData.PassingContainer.NotifyHandlers += HandlePassing;
            eventData.PassingTriggerContainer.NotifyHandlers += HandlePassingTrigger;
            eventData.SubscribeToEventData(MTAEVENTDATA.mtaPassing, uint.MaxValue, false);
            //eventData.SubscribeToEventData(MTAEVENTDATA.mtaPassingTrigger, uint.MaxValue, false);
        }

        public void Dispose()
        {
            Dispose(true);

            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                _mta.Disconnect();
                _mta.Dispose();
                _sdk.Dispose();
            }
        }

        public void ProcessFrom(DateTime time)
        {
            _logger.LogInformation("Processing data from {StartTime}", time);
            Connect(time);
        }
    }
}
