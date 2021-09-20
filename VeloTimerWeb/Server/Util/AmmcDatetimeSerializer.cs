using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using VeloTimerWeb.Server.Models.Mylaps;

namespace VeloTimerWeb.Server.Util
{
    public class AmmcDatetimeSerializer : SerializerBase<DateTime>
    {
        // 2020-09-30T14:03:15.094+0000
        private const string dateFormat = "yyyy-MM-ddTH:mm:ss.fffzzz";

        public override void Serialize(BsonSerializationContext context, BsonSerializationArgs args, DateTime value)
        {
            var dateString = value.ToString(dateFormat, null);

            context.Writer.WriteString(dateString);
        }

        public override DateTime Deserialize(BsonDeserializationContext context, BsonDeserializationArgs args)
        {
            var dateString = context.Reader.ReadString();

            var parsedDate = DateTime.ParseExact(dateString, dateFormat, null);

            return parsedDate;
        }
    }
}
