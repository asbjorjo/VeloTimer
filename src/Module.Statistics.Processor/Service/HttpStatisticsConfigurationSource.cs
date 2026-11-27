using VeloTime.Module.Facilities.Client;
using VeloTime.Module.Statistics.Service.Recompute;
using VeloTime.Module.Timing.Client;

namespace VeloTime.Module.Statistics.Service;

/// <summary>
/// Reads configuration history from Facilities and Timing over HTTP, one call per scope. Used only on a
/// cache miss (<see cref="StatisticsConfigurationHistory"/>).
/// </summary>
public sealed class HttpStatisticsConfigurationSource(ITimingClient timing, IFacilitiesClient facilities) : IStatisticsConfigurationSource
{
    public async Task<InstallationFacilityHistory> GetInstallationAsync(Guid installationId, CancellationToken cancellationToken)
    {
        try
        {
            var installation = await timing.InstallationsGETAsync(installationId, cancellationToken);
            return new() { InstallationId = installationId, FacilityId = installation.FacilityId == Guid.Empty ? null : installation.FacilityId };
        }
        catch (Timing.Client.ApiException exception) when (exception.StatusCode == 404)
        {
            return new() { InstallationId = installationId };
        }
    }

    public async Task<FacilityCourseHistory> GetFacilityCourseAsync(Guid facilityId, CancellationToken cancellationToken)
    {
        try
        {
            var history = await facilities.GetFacilityCourseHistoryAsync(facilityId, cancellationToken);
            return new()
            {
                FacilityId = facilityId,
                Layouts = history.Layouts.Select(layout => new CourseLayoutVersion
                {
                    LayoutId = layout.LayoutId,
                    Assignments = layout.Assignments.Select(assignment => new EffectiveVersion
                    {
                        ValidFromUtc = assignment.ValidFromUtc?.UtcDateTime, ValidToUtc = assignment.ValidToUtc?.UtcDateTime
                    }).ToList(),
                    Revisions = layout.Revisions.Select(revision => new CourseLayoutRevisionVersion
                    {
                        RevisionId = revision.RevisionId,
                        ValidFromUtc = revision.ValidFromUtc?.UtcDateTime,
                        ValidToUtc = revision.ValidToUtc?.UtcDateTime,
                        Segments = revision.Segments.Select(segment => new CourseSegmentVersion
                        {
                            Order = segment.Order, StartPointId = segment.StartPointId, EndPointId = segment.EndPointId,
                            Length = segment.Length
                        }).ToList()
                    }).ToList()
                }).ToList(),
                Associations = history.Associations.Select(association => new TimingAssociationVersion
                {
                    TimingPointId = association.TimingPointId,
                    CoursePointId = association.CoursePointId,
                    ValidFromUtc = association.ValidFromUtc?.UtcDateTime,
                    ValidToUtc = association.ValidToUtc?.UtcDateTime
                }).ToList()
            };
        }
        catch (Facilities.Client.ApiException exception) when (exception.StatusCode == 404)
        {
            return new() { FacilityId = facilityId };
        }
    }
}
