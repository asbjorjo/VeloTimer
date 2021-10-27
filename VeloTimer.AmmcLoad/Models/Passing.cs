using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System;
using VeloTimer.AmmcLoad.Util;

namespace VeloTimer.AmmcLoad.Models
{
    public class Passing
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        [BsonElement("_id")]
        public string Id { get; set; }
        [BsonRepresentation(BsonType.String)]
        [BsonElement("passing_number")]
        public long PassingNumber { get; set; }
        [BsonRepresentation(BsonType.String)]
        [BsonElement("transponder_id")]
        public long TransponderId { get; set; }
        [BsonRepresentation(BsonType.String)]
        [BsonElement("transponder")]
        public string Transponder { get; set; }
        [BsonRepresentation(BsonType.String)]
        [BsonElement("loop_id")]
        public long LoopId { get; set; }
        [BsonRepresentation(BsonType.String)]
        [BsonElement("strength")]
        public int Strength { get; set; }
        [BsonRepresentation(BsonType.String)]
        [BsonElement("strength_text")]
        public string StrengthText { get; set; }
        [BsonSerializer(typeof(AmmcDatetimeSerializer))]
        [BsonElement("utc_time")]
        public DateTimeOffset UtcTime { get; set; }
        [BsonSerializer(typeof(AmmcDatetimeSerializer))]
        [BsonElement("local_time")]
        public DateTimeOffset LocalTime { get; set; }
    }
}
