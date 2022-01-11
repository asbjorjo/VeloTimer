namespace VeloTimer.Shared.Configuration
{
    public class MessageBusSettings
    {
        public static readonly string Section = "MessageBus";

        public string ConnectionString { get; set; }
        public string QueueName { get; set; } = "passings";
        public int MaxConcurrency { get; set; } = 5;
    }
}
