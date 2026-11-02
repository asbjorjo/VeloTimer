using SlimMessageBus.Host;

namespace VeloTime.Agent.Messaging;

internal sealed class OutboxMessageTypeResolver(IMessageTypeResolver inner) : IMessageTypeResolver
{
    public string ToName(Type messageType) => inner.ToName(messageType);

    public Type ToType(string name) => inner.ToType(name)
        ?? throw new InvalidOperationException($"Cannot resolve outbox message type {name}; delivery remains blocked.");
}
