using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;
using System;

namespace VeloTimer.AmmcLoad.Util
{
    public class AmmcDatetimeSerializer : SerializerBase<DateTimeOffset>
    {
        // 2020-09-30T14:03:15.094+0000
        private const string dateFormat = "yyyy-MM-ddTH:mm:ss.fffzzz";

        public override void Serialize(BsonSerializationContext context, BsonSerializationArgs args, DateTimeOffset value)
        {
            var dateString = value.ToString(dateFormat, null);

            context.Writer.WriteString(dateString);
        }

        public override DateTimeOffset Deserialize(BsonDeserializationContext context, BsonDeserializationArgs args)
        {
            var dateString = context.Reader.ReadString();

            var parsedDate = DateTimeOffset.ParseExact(dateString, dateFormat, null);

            return parsedDate;
        }
    }
}
