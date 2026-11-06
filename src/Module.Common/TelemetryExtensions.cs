using SlimMessageBus;

namespace System.Diagnostics;

public static class TelemetryExtensions
{
    public static Activity AddLink(this Activity activity, IConsumerContext context)
    {
        if (context.Headers.TryGetValue("Diagnostic-Id", out var objectId) && objectId is string diagnosticId)
        {
            ActivityContext activityContext = ActivityContext.Parse(diagnosticId, null);
            activity.AddLink(new(activityContext));
        }

        return activity;
    }

    public static Activity? StartActivity(this ActivitySource activitySource, string name, IConsumerContext context)
    {
        var activity = activitySource.StartActivity(name);
        activity?.AddLink(context);
        return activity;
    }
}
