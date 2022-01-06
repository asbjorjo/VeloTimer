namespace VeloTimer.Shared.Hub
{
    public class Strings
    {
        public static string hubUrl = "/hub/passing";

        public static class Events
        {
            public static string RegisterPassing => nameof(IPassingClient.RegisterPassing);
            public static string LastPassing => nameof(IPassingClient.LastPassing);
            public static string NewPassings => nameof(IPassingClient.NewPassings);
        }
    }
}
