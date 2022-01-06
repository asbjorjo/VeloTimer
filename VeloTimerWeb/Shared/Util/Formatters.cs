using System.Globalization;

namespace VeloTimer.Shared.Util
{
    public class Formatters
    {
        public static string FormatTime(double time)
        {
            if (time < 60)
            {
                return string.Format(CultureInfo.InvariantCulture, "{0:00.000}", time);
            }
            else
            {
                var minutes = (int) time / 60;
                var seconds = time % 60;

                return string.Format(CultureInfo.InvariantCulture, "{0:0}:{1:00.000}", minutes, seconds);
            }
        }

        public static string FormatSpeed(double speed)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0:00.000}", speed);
        }
    }
}
