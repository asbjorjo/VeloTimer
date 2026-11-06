using System.Text.Json.Serialization;

namespace VeloTime.Module.Timing.Model
{
    [JsonDerivedType(typeof(MylapsX2Transponder), typeDiscriminator: "MylapsX2")]
    public abstract class Transponder
    {
        public Guid Id { get; init; }
        public virtual TimingSystem System { get; } = TimingSystem.Unknown;
        public required virtual string SystemId { get; init; }
        public virtual string Label { get; set; } = string.Empty;
        public ICollection<TransponderOwner> Owners { get; set; } = new List<TransponderOwner>();

        public void AddOwner(Guid OwnerId, DateTime OwnedFrom, DateTime? OwnedTo = null)
        {
            if (OwnerId == Guid.Empty) throw new ArgumentException(nameof(OwnerId), "Cannot be empty.");
            if (OwnedTo != null && OwnedTo <= OwnedFrom) throw new ArgumentException(nameof(OwnedTo), "Must be greater than OwnedFrom.");

            var ownedToUTC = OwnedTo?.ToUniversalTime();
            var ownedFromUTC = OwnedFrom.ToUniversalTime();

            var existing = Owners.Where(o => o.OwnedTo is null || o.OwnedTo > ownedFromUTC);
            if (ownedToUTC.HasValue)
            {
                existing = existing.Where(o => o.OwnedFrom <= ownedToUTC);
            }
         
            if (existing.Any(o => o.OwnerId != OwnerId))
            {
                throw new InvalidOperationException($"Transponder {Label} already registered.");
            }

            Owners.Add(new TransponderOwner { TransponderId = Id, OwnerId = OwnerId, OwnedFrom = ownedFromUTC, OwnedTo = ownedToUTC });
        }
    }

    public class MylapsX2Transponder : Transponder
    {
        public override TimingSystem System { get; } = TimingSystem.MyLaps_X2;

        public override string Label
        {
            get => string.IsNullOrEmpty(field) ? IdToCode(SystemId) : field;
            set => field = string.IsNullOrEmpty(value) ? IdToCode(SystemId) : value;
        } = string.Empty;

        public static string IdToCode(string Id)
        {
            if (string.IsNullOrEmpty(Id)) return string.Empty;

            if (long.TryParse(Id, out var numericId))
            {
                return IdToCode(numericId);
            }
            else
            {
                throw new ArgumentException(nameof(Id), $"Must be a number - {Id}");
            }
        }

        public static string IdToCode(long Id) => Interface.MylapsX2Code.IdToCode(Id);

        public static long CodeToId(string code) => Interface.MylapsX2Code.CodeToId(code);
    }
}