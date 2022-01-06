using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VeloTimer.Shared.Util;
using Xunit;

namespace Test.Shared.Util
{
    public class FormattersTest
    {
        [Fact]  
        public void FormatTimeLessThanMinute()
        {
            double Time = 0.0;
            string Expected = "00.000";

            string FormattedTime = Formatters.FormatTime(Time);

            Assert.Equal(Expected, FormattedTime);
        }

        [Fact]
        public void FormatTimesMoreThanMinute()
        {
            double Time = 60.0;
            string Expected = "1:00.000";

            string FormattedTime = Formatters.FormatTime(Time);

            Assert.Equal(Expected, FormattedTime);
        }

        [Fact]
        public void FormatSpeed()
        {
            double Speed = 0.0;
            string Expected = "00.000";

            string FormattedSpeed = Formatters.FormatSpeed(Speed);

            Assert.Equal(Expected, FormattedSpeed);
        }
    }
}
