using SlimMessageBus;
using VeloTime.Module.Notifications.Interface.Messages;

namespace VeloTime.Module.Notifications.Processor.Workers;

internal sealed class SlimMessageBusNotificationRefreshPublisher(IMessageBus messageBus)
    : INotificationRefreshPublisher
{
    public Task PublishAsync(Guid eventId, CancellationToken cancellationToken) =>
        messageBus.Publish(new NotificationsRefreshRequestedV1(eventId), cancellationToken: cancellationToken);
}
