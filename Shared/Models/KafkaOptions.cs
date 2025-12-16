namespace Shared.Models
{
    public class KafkaOptions
    {
        public string BootstrapServers { get; set; } = "localhost:9092";
        public string? ClientId { get; set; }
        public string? SaslUsername { get; set; }
        public string? SaslPassword { get; set; }
    }
}