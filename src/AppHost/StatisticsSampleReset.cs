using Aspire.Hosting.ApplicationModel;

public static class StatisticsSampleReset
{
    public static void Configure<T>(IResourceBuilder<T> migrator,
        IResourceBuilder<IResourceWithConnectionString> database, bool isRunMode, string? approval)
        where T : IResourceWithEnvironment
    {
        // The local database volume and external broker have independent lifetimes.
        // Only an operator can attest that the activation barrier is satisfied.
        if (isRunMode && approval == "approved")
        {
            migrator.WithEnvironment("ConnectionStrings__velotimedb",
                ReferenceExpression.Create($"{database};Options=-c velotime.statistics_sample_reset=approved"));
        }
    }
}
