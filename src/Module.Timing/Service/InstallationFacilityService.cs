using Microsoft.EntityFrameworkCore;
using SlimMessageBus;
using VeloTime.Module.Timing.Interface.Data;
using VeloTime.Module.Timing.Interface.Messages;
using VeloTime.Module.Timing.Model;
using VeloTime.Module.Timing.Storage;

namespace VeloTime.Module.Timing.Service;

/// <summary>
/// Edits an installation's dated facility assignments (plan §4.2, C2). An edit sets the facility from an
/// effective instant onward: assignments starting at or after it are replaced, the one spanning it is closed
/// there, and a new open-ended assignment starts there. Earlier history is never touched.
/// </summary>
public static class InstallationFacilityService
{
    /// <summary>
    /// Sets <paramref name="installation"/>'s facility from <paramref name="fromUtc"/> (null: its whole history)
    /// onward; a null <paramref name="facilityId"/> leaves it unassigned from then. Changes are tracked on
    /// <paramref name="storage"/> and <see cref="InstallationFacilityChangedV1"/> is published on
    /// <paramref name="bus"/>, which must be the outbox bus of the caller's open transaction. The caller saves
    /// and commits. Returns false when the edit changes nothing.
    /// </summary>
    public static async Task<bool> AssignAsync(TimingDbContext storage, IMessageBus? bus, Installation installation,
        Guid? facilityId, DateTime? fromUtc, CancellationToken cancellationToken)
    {
        if (facilityId == Guid.Empty) facilityId = null;
        if (fromUtc is { } from) fromUtc = DateTime.SpecifyKind(from.Kind == DateTimeKind.Local ? from.ToUniversalTime() : from, DateTimeKind.Utc);
        var assignments = await storage.Set<InstallationFacilityAssignment>()
            .Where(a => a.InstallationId == installation.Id).ToListAsync(cancellationToken);
        var affected = assignments.Where(a => Overlaps(a, fromUtc)).ToList();
        var unchanged = facilityId is { } target
            ? affected.Count == 1 && affected[0].FacilityId == target && affected[0].ValidToUtc is null && Covers(affected[0], fromUtc)
            : affected.Count == 0;
        if (unchanged) return false;

        foreach (var assignment in affected)
        {
            if (Starts(assignment, fromUtc)) storage.Remove(assignment);
            else assignment.ValidToUtc = fromUtc;
        }
        if (facilityId is { } facility)
            storage.Add(new InstallationFacilityAssignment
            {
                Id = Guid.NewGuid(), InstallationId = installation.Id, FacilityId = facility, ValidFromUtc = fromUtc
            });
        installation.Facility = facilityId ?? Guid.Empty;
        if (bus is null)
            throw new InvalidOperationException("Changing an installation's facility requires the Timing outbox bus.");
        await bus.Publish(new InstallationFacilityChangedV1(Guid.NewGuid(), installation.Id, fromUtc, null),
            cancellationToken: cancellationToken);
        return true;
    }

    public static InstallationFacilityAssignmentDTO ToDto(this InstallationFacilityAssignment assignment) =>
        new(assignment.Id, assignment.InstallationId, assignment.FacilityId, assignment.ValidFromUtc, assignment.ValidToUtc);

    /// <summary>True when the assignment covers any instant at or after <paramref name="from"/>.</summary>
    private static bool Overlaps(InstallationFacilityAssignment assignment, DateTime? from) =>
        from is not { } start || assignment.ValidToUtc is not { } end || end > start;

    /// <summary>True when the assignment already holds at <paramref name="from"/> (null: from the beginning).</summary>
    private static bool Covers(InstallationFacilityAssignment assignment, DateTime? from) =>
        assignment.ValidFromUtc is not { } begin || from is { } start && begin <= start;

    /// <summary>True when the assignment starts at or after <paramref name="from"/> (so the edit replaces all of it).</summary>
    private static bool Starts(InstallationFacilityAssignment assignment, DateTime? from) =>
        from is not { } start ? true : assignment.ValidFromUtc is { } begin && begin >= start;
}
