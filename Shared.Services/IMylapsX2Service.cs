namespace VeloTimer.Shared.Services
{
    public interface IMylapsX2Service
    {
        void ProcessFrom(DateTime time);
        void ProcessQueue();
    }
}
