using MediatR;
using VeloTimer.PassingLoader.Services.Api;
using VeloTimer.Shared.Data.Models.Timing;

namespace PassingLoader.Services.MediatR.Handlers
{
    public class GetMostRecentPassingAtTrackHandler : IRequestHandler<GetMostRecentPassingAtTrackQuery, PassingWeb>
    {
        private readonly ApiService _apiService;

        public GetMostRecentPassingAtTrackHandler(ApiService apiService) => _apiService = apiService;

        public async Task<PassingWeb> Handle(GetMostRecentPassingAtTrackQuery request, CancellationToken cancellationToken)
        {
            PassingWeb passing = await _apiService.GetMostRecentPassing(request.TrackSlug);

            return passing;
        }
    }
}
