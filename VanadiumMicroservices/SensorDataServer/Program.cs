using Data.Mongo;
using MongoDB.Driver;
using SensorDataSaver;
using Microsoft.EntityFrameworkCore;
using Shared.Models;
using Shared.ServicesHelpers;

var builder = Host.CreateApplicationBuilder(args);

// Configure RabbitMQ options
builder.Services.Configure<RabbitMQOptions>(
    builder.Configuration.GetSection("RabbitMQOptions"));

// Register HttpClient for HTTP requests
builder.Services.AddHttpClient();

// Register sensor data saver as both a singleton and hosted service
builder.Services.AddHostedService<SensorDataSaver.SensorDataSaver>();
builder.Services.AddSingleton<ISensorDataSaver>(provider => 
    provider.GetRequiredService<SensorDataSaver.SensorDataSaver>());
builder.Services.AddScoped<IPanelReadingRepository, PanelReadingRepository>();

builder.Services.AddSingleton<IMongoClient>(sp =>
{
    var connectionString = builder.Configuration.GetSection("Mongo")["ConnectionString"];
    if (string.IsNullOrEmpty(connectionString))
    {
        throw new InvalidOperationException("MongoDB connection string is not configured. Please set 'Mongo:ConnectionString' in appsettings.json");
    }
    return new MongoClient(connectionString);
});

builder.Services.AddScoped<MongoDbInitializer>();

await WaitForServicesHelper.WaitForSensorInfoAsync(new List<string> {  builder.Configuration.GetSection("SensorInfoServer")["BaseUrl"] }, CancellationToken.None);

var logger = builder.Services.BuildServiceProvider().GetRequiredService<ILogger<Program>>();
logger.LogInformation("SensorInfoServer is ready");

var host = builder.Build();

// Run Mongo initializer
using (var scope = host.Services.CreateScope())
{
    var initializer = scope.ServiceProvider.GetRequiredService<MongoDbInitializer>();
    await initializer.InitializeAsync();
}
host.Run();
