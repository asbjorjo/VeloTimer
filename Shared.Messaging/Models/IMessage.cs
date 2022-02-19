namespace VeloTime.Shared.Messaging
{
    public interface IMessage
    {
        public string Id { get; }
        public string Subject { get; }
        public string Group { get; }
        public object Content { get; }
    }
}
