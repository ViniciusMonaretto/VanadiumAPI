namespace Shared.Models
{
    public class RabbitMQOptions
    {
        public string HostName { get; set; } = "localhost";
        public int Port { get; set; } = 5672;
        public string? UserName { get; set; }
        public string? Password { get; set; }
        public string? VirtualHost { get; set; } = "/";
        public string? QueueName { get; set; } // Optional queue name for consumers
    }
}


