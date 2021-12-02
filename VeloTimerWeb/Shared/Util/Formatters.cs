using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VeloTimer.Shared.Util
{
    public class Formatters
    {
        public static string FormatTime(double time)
        {
            if (time < 60)
            {
                return string.Format("{0:00.000}", time);
            }
            else
            {
                var minutes = time / 60;
                var seconds = time % 60;

                return string.Format("{0:0}:{1:00.000}", minutes, seconds);
            }
        }
    }
}
