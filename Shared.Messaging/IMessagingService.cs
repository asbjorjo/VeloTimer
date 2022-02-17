using VeloTimer.Shared.Data.Models.Timing;

namespace VeloTime.Shared.Messaging
{
    public interface IMessagingService
    {
        Task SendMessage(IMessage message);
        Task SendMessages(IEnumerable<IMessage> messages);
    }
}
