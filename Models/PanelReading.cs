using MongoDB.Bson.Serialization.Attributes;

namespace Models
{
    public class PanelReading
    {
        [BsonId]
        public int  Id { get; set; }
        public int PanelId {get; set;}
        public DateTime ReadingTime {get; set;}
    }
}