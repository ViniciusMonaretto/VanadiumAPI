using Data.Sqlite;
using Microsoft.EntityFrameworkCore;
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

var app = builder.Build();

app.UseCors("AllowAngular");
app.MapHub<ClientHub>("/ioCloudApi");
app.MapGet("/", () => "Hello World!");

app.Run();

