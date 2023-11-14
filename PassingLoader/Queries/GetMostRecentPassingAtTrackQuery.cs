using MediatR;
using VeloTimer.Shared.Data.Models.Timing;

namespace VeloTimer.PassingLoader.Queries;
public record GetMostRecentPassingAtTrackQuery(string TrackSlug) : IRequest<PassingWeb>;