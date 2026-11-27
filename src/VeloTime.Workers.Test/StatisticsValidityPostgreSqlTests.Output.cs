using Xunit.Abstractions;

namespace VeloTime.Workers.Test;

public sealed partial class StatisticsValidityPostgreSqlTests
{
    private readonly ITestOutputHelper output;

    public StatisticsValidityPostgreSqlTests(ITestOutputHelper output) => this.output = output;
}
