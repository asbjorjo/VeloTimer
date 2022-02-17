namespace VeloTime.Shared.Messaging
{
    public interface IMessage
    {
        public string Id { get; }
        public string Topic { get; }
        public string Subject { get; }
        public object Content { get; }
    }
}
