using MediatR;
using VeloTimer.Shared.Data.Models.Timing;

public record GetMostRecentPassingAtTrackQuery(string TrackSlug) : IRequest<PassingWeb>;