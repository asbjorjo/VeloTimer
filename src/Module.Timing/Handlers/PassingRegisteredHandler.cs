using Microsoft.EntityFrameworkCore;
using SlimMessageBus;
using VeloTime.Module.Timing.Interface.Messages;
using VeloTime.Module.Timing.Model;
using VeloTime.Module.Timing.Storage;

namespace VeloTime.Module.Timing.Handlers;

public class PassingObservedHandler(TimingDbContext storage, IMessageBus messageBus) : IConsumer<PassingObserved>
{
    public async Task OnHandle(PassingObserved message, CancellationToken token)
    {
        TimingPoint timingPoint = await storage.Set<TimingPoint>().SingleAsync(t => t.Installation.Id == message.Installation && t.Description == message.TimingPoint);
        Transponder transponder = await storage.Set<Transponder>().SingleAsync(t => t.SystemId == message.TransponderId && t.Type.Name == message.TransponderType);

        Passing passing = new() { Id = Guid.NewGuid(), Time = message.Time.ToUniversalTime().DateTime, TimingPoint = timingPoint, Transponder = transponder };

        await storage.Set<Passing>().AddAsync(passing);
        await storage.SaveChangesAsync();

        await messageBus.Publish(new PassingSaved(passing.Time, passing.Transponder.Id, passing.TimingPoint.Id), cancellationToken: token);
    }
}
