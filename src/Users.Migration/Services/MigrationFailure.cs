using System.Data.Common;
using System.Net.Http;
using System.Net.Sockets;
using KeycloakApiException = VeloTime.Users.Migration.Keycloak.ApiException;
using StatisticsApiException = VeloTime.Module.Statistics.Client.ApiException;
using TimingApiException = VeloTime.Module.Timing.Client.ApiException;

namespace VeloTime.Users.Migration.Services;

internal static class MigrationFailure
{
    internal static bool IsFatalOrStopping(Exception exception, CancellationToken ct) =>
        (ct.IsCancellationRequested && exception is OperationCanceledException) || IsFatal(exception);

    internal static bool IsFatal(Exception exception)
    {
        if (exception is HttpRequestException or SocketException or TimeoutException or
            TaskCanceledException or OperationCanceledException or DbException or Polly.ExecutionRejectedException)
            return true;

        if (exception is KeycloakApiException keycloakException)
            return IsServiceFailure(keycloakException.StatusCode);
        if (exception is StatisticsApiException statisticsException)
            return IsServiceFailure(statisticsException.StatusCode);
        if (exception is TimingApiException timingException)
            return IsServiceFailure(timingException.StatusCode);

        return exception is InvalidOperationException { InnerException: DbException };
    }

    private static bool IsServiceFailure(int statusCode) =>
        statusCode is 401 or 403 || statusCode >= 500;
}
