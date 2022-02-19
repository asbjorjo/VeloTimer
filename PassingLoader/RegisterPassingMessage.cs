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
        public string Subject { get => "register"; }
        public string Group { get => Passing.TransponderId; }
        public object Content { get => Passing; }
    }
}
