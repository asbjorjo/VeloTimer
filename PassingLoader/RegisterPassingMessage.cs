using VeloTime.Shared.Messaging;
using VeloTimer.Shared.Data.Models.Timing;

namespace VeloTime.PassingLoader
{
    public class RegisterPassingMessage : IMessage
    {
        public RegisterPassingMessage(PassingRegister passing)
        {
            this.passing = passing;
        }

        public PassingRegister passing { get; private set; }

        public string Id { get => $"{passing.Track}_{passing.LoopId}_{passing.TransponderId}_{passing.Time.UtcDateTime.Ticks}"; }
        public string Topic { get => passing.TransponderId; }
        public string Subject { get => "register"; }
        public object Content { get => passing; }
    }
}
