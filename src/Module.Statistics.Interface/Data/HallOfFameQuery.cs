namespace VeloTime.Module.Statistics.Interface.Data;

/// <summary>Shared structural validation for full-document Hall of Fame settings writes.</summary>
public static class HallOfFameQuery
{
    public const int DefaultRowCount = 10;
    public const int MinimumRowCount = 1;
    public const int MaximumRowCount = 100;

    /// <summary>
    /// Keys match the JSON request property names and can be returned as a 400 ValidationProblem.
    /// Item existence and authorization are validated by the owning API.
    /// </summary>
    public static Dictionary<string, string[]> Validate(SaveHallOfFameSettingsRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (request.ExpectedVersion < 0)
            errors["expectedVersion"] = ["Expected version must be zero or greater."];

        if (request.Sections is null)
        {
            errors["sections"] = ["Sections are required; use an empty list to configure no sections."];
            return errors;
        }

        var sectionKeys = new HashSet<(Guid ItemId, HallOfFameRankingMode Mode)>();
        for (var index = 0; index < request.Sections.Count; index++)
        {
            var section = request.Sections[index];
            var prefix = $"sections[{index}]";
            if (section is null)
            {
                errors[prefix] = ["A section is required."];
                continue;
            }

            if (section.StatisticsItemId == Guid.Empty)
                errors[$"{prefix}.statisticsItemId"] = ["A nonempty statistics item ID is required."];
            if (!Enum.IsDefined(section.RankingMode))
                errors[$"{prefix}.rankingMode"] = ["Ranking mode must be BestTime or MostPassings."];
            if (section.RowCount is < MinimumRowCount or > MaximumRowCount)
                errors[$"{prefix}.rowCount"] = [$"Row count must be between {MinimumRowCount} and {MaximumRowCount}."];

            if (section.StatisticsItemId != Guid.Empty && Enum.IsDefined(section.RankingMode) &&
                !sectionKeys.Add((section.StatisticsItemId, section.RankingMode)))
                errors[prefix] = ["Only one section per statistics item and ranking mode is allowed."];

            if (section.Columns is null)
            {
                errors[$"{prefix}.columns"] = ["Columns are required; use an empty list for no optional columns."];
                continue;
            }

            var columns = new HashSet<HallOfFameColumn>();
            for (var columnIndex = 0; columnIndex < section.Columns.Count; columnIndex++)
            {
                var column = section.Columns[columnIndex];
                if (!Enum.IsDefined(column))
                    errors[$"{prefix}.columns[{columnIndex}]"] = ["Unsupported Hall of Fame column."];
                else if (!columns.Add(column))
                    errors[$"{prefix}.columns[{columnIndex}]"] = ["A column may appear only once in a section."];
            }
        }

        return errors;
    }
}
