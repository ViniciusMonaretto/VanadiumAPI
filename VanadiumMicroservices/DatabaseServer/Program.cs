using Data.Mongo;
using MongoDB.Driver;
using SensorDataSaver;
using Data.Sqlite;
using Microsoft.EntityFrameworkCore;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddDbContext<SqliteDataContext>(options =>
    options.UseSqlite("Data Source=app.db"));

builder.Services.AddScoped<IPanelInfoRepository, PanelInfoRepository>();

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

var host = builder.Build();

// Run Mongo initializer
using (var scope = host.Services.CreateScope())
{
    var initializer = scope.ServiceProvider.GetRequiredService<MongoDbInitializer>();
    await initializer.InitializeAsync();
}
host.Run();
