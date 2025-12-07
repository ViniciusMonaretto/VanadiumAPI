using Data.Mongo;
using Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MongoDB.Driver;
using Services;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSignalR();
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAngular",
        policy => policy
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials()
            .WithOrigins("http://localhost:4200"));
});

builder.Services.AddDbContext<SqliteDataContext>(options =>
    options.UseSqlite("Data Source=app.db"));

builder.Services.AddScoped<IPanelInfoRepository, PanelInfoRepository>();
// Configure MQTT options
builder.Services.Configure<MqttOptions>(
    builder.Configuration.GetSection("MqttOptions"));

// Register MQTT service as both a singleton and hosted service
builder.Services.AddSingleton<MqttService>();
builder.Services.AddSingleton<IMqttService>(provider => 
    provider.GetRequiredService<MqttService>());
builder.Services.AddHostedService<MqttService>(provider => 
    provider.GetRequiredService<MqttService>());

// Register sensor data saver as both a singleton and hosted service
builder.Services.AddHostedService<SensorDataSaver>();
builder.Services.AddSingleton<ISensorDataSaver>(provider => 
    provider.GetRequiredService<SensorDataSaver>());
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

var app = builder.Build();

// Run Mongo initializer
using (var scope = app.Services.CreateScope())
{
    var initializer = scope.ServiceProvider.GetRequiredService<MongoDbInitializer>();
    await initializer.InitializeAsync();
}

app.UseCors("AllowAngular");
app.MapHub<ClientHub>("/ioCloudApi");
app.MapGet("/", () => "Hello World!");

app.Run();

