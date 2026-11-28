namespace VeloTime.Module.Facilities.Service;

/// <summary>A half-open UTC interval [From, To); null bounds are unbounded.</summary>
public readonly record struct Interval(DateTimeOffset? From, DateTimeOffset? To)
{
    public bool IsEmpty => From is { } from && To is { } to && from >= to;
}

/// <summary>Interval arithmetic for effective-dated configuration (docs/replay-safe-statistics-plan.md §4.1).</summary>
public static class IntervalMath
{
    public static bool Overlaps(Interval left, Interval right) =>
        !Intersect(left, right).IsEmpty;

    public static Interval Intersect(Interval left, Interval right) =>
        new(MaxLower(left.From, right.From), MinUpper(left.To, right.To));

    /// <summary>The parts of <paramref name="existing"/> left after removing <paramref name="cut"/>.</summary>
    public static (Interval? Left, Interval? Right) Remainder(Interval existing, Interval cut)
    {
        Interval? left = cut.From.HasValue && (!existing.From.HasValue || existing.From.Value < cut.From.Value)
            ? new Interval(existing.From, cut.From)
            : null;
        Interval? right = cut.To.HasValue && (!existing.To.HasValue || cut.To.Value < existing.To.Value)
            ? new Interval(cut.To, existing.To)
            : null;
        return (left, right);
    }

    /// <summary>The parts of <paramref name="window"/> not covered by any of <paramref name="cuts"/>, in order.</summary>
    public static List<Interval> Subtract(Interval window, IEnumerable<Interval> cuts)
    {
        var relevant = cuts.Select(cut => Intersect(window, cut)).Where(cut => !cut.IsEmpty)
            .OrderBy(cut => cut.From.HasValue).ThenBy(cut => cut.From).ToList();
        var result = new List<Interval>();
        var cursor = window.From;
        var cursorAtNegativeInfinity = !window.From.HasValue;
        foreach (var cut in relevant)
        {
            var cutStartsAfterCursor = cut.From.HasValue && (cursorAtNegativeInfinity || cursor!.Value < cut.From.Value);
            if (cutStartsAfterCursor) result.Add(new Interval(cursorAtNegativeInfinity ? null : cursor, cut.From));
            if (!cut.To.HasValue) return result;
            if (cursorAtNegativeInfinity || cut.To.Value > cursor!.Value)
            {
                cursor = cut.To;
                cursorAtNegativeInfinity = false;
            }
        }
        var tail = new Interval(cursorAtNegativeInfinity ? null : cursor, window.To);
        if (!tail.IsEmpty) result.Add(tail);
        return result;
    }

    /// <summary>The smallest interval containing all of <paramref name="intervals"/>; (null, null) when there are none.</summary>
    public static Interval Hull(IEnumerable<Interval> intervals)
    {
        var any = false;
        DateTimeOffset? from = null, to = null;
        bool openFrom = false, openTo = false;
        foreach (var interval in intervals)
        {
            any = true;
            if (interval.From is not { } lower) openFrom = true;
            else if (from is null || lower < from) from = lower;
            if (interval.To is not { } upper) openTo = true;
            else if (to is null || upper > to) to = upper;
        }
        return any ? new Interval(openFrom ? null : from, openTo ? null : to) : new Interval(null, null);
    }

    private static DateTimeOffset? MaxLower(DateTimeOffset? left, DateTimeOffset? right) =>
        !left.HasValue ? right : !right.HasValue ? left : left.Value >= right.Value ? left : right;

    private static DateTimeOffset? MinUpper(DateTimeOffset? left, DateTimeOffset? right) =>
        !left.HasValue ? right : !right.HasValue ? left : left.Value <= right.Value ? left : right;
}
