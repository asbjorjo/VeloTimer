using System.Globalization;
using Microsoft.Extensions.Localization;
using VeloTime.Module.Notifications.Interface;
using VeloTime.Module.Notifications.Interface.Data;

namespace VeloTime.WebUI.Mud.Client.Services;

public static class NotificationLabels
{
    public static NotificationTextDTO Text(NotificationContentDTO content) =>
        CultureInfo.CurrentUICulture.Name == "nb-NO" ? content.NorwegianBokmal : content.English;
    public static string Scope(NotificationDismissalScope scope, int? seconds,
        IStringLocalizer<SharedResource> strings, CultureInfo culture) => scope switch
        {
            NotificationDismissalScope.Session => strings["NotificationsHideSession"],
            NotificationDismissalScope.Permanent => strings["NotificationsHidePermanent"],
            _ => strings["NotificationsHideDuration", Duration(seconds ?? 0, strings, culture)]
        };
    public static string Duration(int seconds, IStringLocalizer<SharedResource> strings, CultureInfo culture) =>
        seconds % 86400 == 0 ? strings["NotificationsDays", (seconds / 86400).ToString("N0", culture)] :
        seconds % 3600 == 0 ? strings["NotificationsHours", (seconds / 3600).ToString("N0", culture)] :
        strings["NotificationsSeconds", seconds.ToString("N0", culture)];
}
