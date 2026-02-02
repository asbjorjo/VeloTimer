using SlimMessageBus;
using SlimMessageBus.Host.Interceptor;
using System.Diagnostics;

namespace VeloTime.Module.Common;

internal class ActivityInterceptor<TMessage> : IConsumerInterceptor<TMessage>
{
    private static readonly ActivitySource activitySource = new("VeloTime.Module.Common.ActivityInterceptor");

    public async Task<object> OnHandle(TMessage message, Func<Task<object>> next, IConsumerContext context)
    {
        using var activity = activitySource.StartActivity(typeof(TMessage).Name, ActivityKind.Consumer);
        activity?.AddLink(context);
        return await next();
    }
}
