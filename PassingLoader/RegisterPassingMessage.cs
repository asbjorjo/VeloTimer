using VeloTime.Shared.Messaging;
using VeloTimer.Shared.Data.Models.Timing;

namespace VeloTime.PassingLoader
{
    public class RegisterPassingMessage : IMessage
    {
        public RegisterPassingMessage(PassingRegister passing)
        {
            this.Passing = passing;
        }

        public PassingRegister Passing { get; private set; }

        public string Id { get => $"{Passing.Track}_{Passing.LoopId}_{Passing.TransponderId}_{Passing.Time.UtcDateTime.Ticks}"; }
        public string Topic { get => Passing.TransponderId; }
        public string Subject { get => "register"; }
        public object Content { get => Passing; }
    }
}
