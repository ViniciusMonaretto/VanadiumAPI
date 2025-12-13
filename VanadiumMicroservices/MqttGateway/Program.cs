using Mqtt;
using Shared.Models;

var builder = Host.CreateApplicationBuilder(args);

// Configure MQTT options
builder.Services.Configure<MqttOptions>(
    builder.Configuration.GetSection("MqttOptions"));

builder.Services.AddSingleton<MqttService>();
builder.Services.AddSingleton<IMqttService>(provider => 
    provider.GetRequiredService<MqttService>());
builder.Services.AddHostedService<MqttService>(provider => 
    provider.GetRequiredService<MqttService>());

// Configure Kafka options
builder.Services.Configure<KafkaOptions>(
    builder.Configuration.GetSection("KafkaOptions"));

var host = builder.Build();
host.Run();
