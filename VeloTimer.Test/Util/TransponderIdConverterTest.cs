using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VeloTimer.Shared.Util;
using Xunit;

namespace VeloTimer.Test.Shared.Util
{
    public class TransponderIdConverterTest
    {
        [Fact]
        public void CodeToId()
        {
            string Code = "HT-24422";
            long Expected = 106187718;

            long Id = TransponderIdConverter.CodeToId(Code);
            
            Assert.Equal(Expected, Id);
        }

        [Fact]
        public void LongIdToCode()
        {
            long Id = 106187718;
            string Expected = "HT-24422";

            string Code = TransponderIdConverter.IdToCode(Id);

            Assert.Equal(Expected, Code);
        }

        [Fact]
        public void InvalidStringIdToCode()
        {
            string Id = "HT-24422";
            string Expected = string.Empty;

            string Code = TransponderIdConverter.IdToCode(Id);

            Assert.Equal(Expected, Code);
        }

        [Fact]
        public void InvalidCodeToId()
        {
            string Code = "AA-12345";
            long Expected = -1;

            long Id = TransponderIdConverter.CodeToId(Code);

            Assert.Equal(Expected, Id);
        }
    }
}
