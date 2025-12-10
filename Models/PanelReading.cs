using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Models
{
    public class PanelReading
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string? Id { get; set; }
        public int PanelId {get; set;}
        public DateTime ReadingTime {get; set;}
        public float Value {get; set;}
    }
}