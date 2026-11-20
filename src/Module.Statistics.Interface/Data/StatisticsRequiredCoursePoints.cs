namespace VeloTime.Module.Statistics.Interface.Data;

/// <summary>
/// Replacement value for intermediate requirements on a Simple configuration. Nonempty lists
/// require a nonempty layout ID and unique, nonempty point IDs, distinct from both endpoints.
/// An empty list requires a null layout ID and represents no requirements. Null lists are invalid.
/// </summary>
/// <remarks>
/// The layout and all points must belong to the endpoints' facility. Sort its segments by unique
/// Order, require a continuous forward chain, and reject ambiguous paths: a point may occur only
/// once, except that a closed layout's final point repeats its first. On an open layout, start
/// must precede end. On a closed layout, use the forward path from start to end with at most one
/// wrap; equal start and end means one complete lap. The list must contain only intermediate
/// points on that path, in strictly increasing forward path order, not arbitrary selection order.
/// Reject missing, duplicated, off-path, reversed, or otherwise ambiguous references explicitly.
/// Compare layout IDs and point IDs by value and sequence when detecting structural changes;
/// collection reference or record equality is insufficient.
/// Layout changes must not silently reinterpret saved requirements: protect referenced paths from
/// structural changes or preserve and validate an immutable path snapshot before enforcement.
///
/// Requirements are structural and cannot be changed after this Simple configuration or a
/// dependent Multi has results. No requirements preserves legacy processing. Missing or out-of-order
/// observations retain an invalid entry; later arrivals do not repair it. The checkpoint outcome
/// must be retained independently of the displayed reason for threshold revalidation.
/// </remarks>
public sealed record StatisticsRequiredCoursePoints(Guid? CourseLayoutId, IReadOnlyList<Guid>? CoursePointIds);
