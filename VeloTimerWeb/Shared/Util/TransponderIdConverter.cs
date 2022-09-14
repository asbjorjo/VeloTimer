using System;

namespace VeloTimer.Shared.Util
{
    public class TransponderIdConverter
    {
        private const int idOffset = 0x6000000;
        private const int numericOffset = 100000;

        private static readonly char[] Characters = { 'C', 'F', 'G', 'H', 'K', 'L', 'N', 'P', 'R', 'S', 'T', 'V', 'W', 'X', 'Z' };

        public static string IdToCode(string Id)
        {
            if (long.TryParse(Id, out var numericId))
            {
                return IdToCode(numericId);
            }

            return string.Empty;
        }

        public static string IdToCode(long Id)
        {
            string code = string.Empty;

            if (Id <= 0)
                return code;

            if (Id > idOffset)
            {
                string prefix = "";
                var n = Id - idOffset;
                var m = n / numericOffset;

                for (int i = 2; i >= 0; i--)
                {
                    var c = Characters[m % 15];
                    if ((c != 'C') || (i > 0))
                    {
                        prefix = c + prefix;
                        m /= 15;
                    }
                }

                code = $"{prefix}-{n % numericOffset:00000}";
            }

            return code;
        }

        public static long CodeToId(string code)
        {
            long id = -1;

            if (string.IsNullOrWhiteSpace(code))
                return id;

            if (code.IndexOf("-") > 0)
                code = code.ToUpper().Remove(code.IndexOf("-"), 1);

            if (code.Length == 7)
            {
                code = "C" + code;
            }

            if (code.Length == 8 && int.TryParse(code.Substring(3, 5), out _))
            {
                var codeArray = code.ToCharArray();

                if (Array.Exists(Characters, c => codeArray[0] == c)
                    && Array.Exists(Characters, c => codeArray[1] == c)
                    && Array.Exists(Characters, c => codeArray[2] == c))
                {
                    id = numericOffset
                         * (Array.IndexOf(Characters, codeArray[0]) * 225
                            + Array.IndexOf(Characters, codeArray[1]) * 15
                            + Array.IndexOf(Characters, codeArray[2]));
                    id += int.Parse(code.Substring(3, 5));
                    id += idOffset;
                }
            }

            return id;
        }
    }
}
