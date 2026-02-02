using Npgsql.Replication;

namespace VeloTime.Module.Timing.Model
{
    public abstract class Transponder
    {
        private string? _label;
        public Guid Id { get; init; }
        public readonly TimingSystem System;
        public string SystemId { get; init; }
        public string Label { get; set; }
    }

    public class MylapsX2Transponder : Transponder
    {
        private const int idOffset = 0x6000000;
        private const int numericOffset = 100000;

        private static readonly char[] Characters = { 'C', 'F', 'G', 'H', 'K', 'L', 'N', 'P', 'R', 'S', 'T', 'V', 'W', 'X', 'Z' };

        public new readonly TimingSystem System = TimingSystem.MyLaps_X2;

        public MylapsX2Transponder(string SystemId)
        {
            this.SystemId = SystemId;
            Label = IdToCode(SystemId);
        }

        public static string IdToCode(string Id)
        {
            if (long.TryParse(Id, out var numericId))
            {
                return IdToCode(numericId);
            }
            else
            {
                throw new ArgumentException(nameof(Id), "Must be a number.");
            }
        }

        public static string IdToCode(long Id)
        {
            string code = string.Empty;

            if (Id <= 0 || Id <= idOffset)
                throw new ArgumentOutOfRangeException(nameof(Id), $"Must be a positive number greather than {idOffset}.");

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

            if (string.IsNullOrWhiteSpace(code)) throw new ArgumentOutOfRangeException(nameof(code), "Cannot be empty");

            if (code.IndexOf("-") > 0)
                code = code.ToUpper().Remove(code.IndexOf("-"), 1);

            if (code.Length == 7)
            {
                code = "C" + code;
            }

            var codeArray = code.ToCharArray();

            if (code.Length != 8) throw new ArgumentException(nameof(code), "Must be 8 characters");
            if (!code.Substring(3, 5).All(char.IsDigit)) throw new ArgumentOutOfRangeException(paramName: nameof(code), actualValue: code, "Last 5 characters must be numbers.");
            if (code.Substring(0, 3).Except(Characters).Any()) throw new ArgumentOutOfRangeException(paramName: nameof(code), actualValue: code, "Contains illegal characters.");

            id = numericOffset
                    * (Array.IndexOf(Characters, codeArray[0]) * 225
                    + Array.IndexOf(Characters, codeArray[1]) * 15
                    + Array.IndexOf(Characters, codeArray[2]));
            id += int.Parse(code.Substring(3, 5));
            id += idOffset;

            return id;
        }
    }
}