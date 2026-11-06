namespace VeloTime.Module.Notifications.Processor.Workers;

public interface INotificationRefreshPublisher
{
    Task PublishAsync(Guid eventId, CancellationToken cancellationToken);
}
