namespace VeloTimer.AmmcLoad.Data
{
    public class PassingDatabaseSettings : IPassingDatabaseSettings
    {
        public string PassingCollection { get; set; }
        public string PassingDatabase { get; set; }
        public string ConnectionString { get; set; }
    }

    public interface IPassingDatabaseSettings
    {
        string PassingCollection { get; set; }
        string PassingDatabase { get; set; }
        string ConnectionString { get; set; }
    }
}
