namespace VeloTime.Shared.Messaging
{
    public class Message
    {
        public string Id { get; set; }
        public string Topic { get; set; }
        public string Subject { get; set; }
        public object Content { get; set; }
    }
}
