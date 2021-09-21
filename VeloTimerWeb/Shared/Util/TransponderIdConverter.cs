using System;

namespace VeloTimer.Shared.Util
{
    public class TransponderIdConverter
    {
        public static char[] Characters { get; } = { 'C', 'F', 'G', 'H', 'K', 'L', 'N', 'P', 'R', 'S', 'T', 'V', 'W', 'X', 'Z' };

        public static string IdToCode(long Id)
        {
            string code = "";

            if (Id > 0x6000000)
            {
                string prefix = "";
                var n = Id - 0x6000000;
                var m = n / 100000;

                for (int i = 2; i >= 0; i--)
                {
                    var c = Characters[m % 15];
                    if ((c != 'C') || (i > 0))
                    {
                        prefix = c + prefix;
                        m = m / 15;
                    }
                }
                var suffix = String.Format("{0:00000}", n % 100000);

                code = $"{prefix}-{suffix}";
            }

            return code;
        }

        public static long CodeToId(string code)
        {
            long id = -1;

            code = code.ToUpper().Remove(code.IndexOf("-"), 1);

            if (code.Length == 7)
            {
                code = "C" + code;
            }

            var codeArray = code.ToCharArray();

            if (Array.Exists(Characters, c => codeArray[0] == c)
                && Array.Exists(Characters, c => codeArray[1] == c)
                && Array.Exists(Characters, c => codeArray[2] == c))
            {
                id = 100000
                     * (Array.IndexOf(Characters, codeArray[0]) * 225
                        + Array.IndexOf(Characters, codeArray[1]) * 15
                        + Array.IndexOf(Characters, codeArray[2]));
                id += int.Parse(code.Substring(3, 5));
                id += 0x6000000;
            }

            return id;
        }
    }
}
