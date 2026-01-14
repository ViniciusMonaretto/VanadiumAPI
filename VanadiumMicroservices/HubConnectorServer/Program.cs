using API.Hubs;
using API.Services;
using HubConnectorServer;
using Shared.Models;
using Shared.ServicesHelpers;

var builder = WebApplication.CreateBuilder(args);

// Explicitly configure the URL to ensure it runs on port 5010
builder.WebHost.UseUrls("http://localhost:5010");

// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Add SignalR service
builder.Services.AddSignalR();
builder.Services.AddHostedService<RabbitMQConsumerService>();
builder.Services.AddSingleton<IPanelBroadcastService, PanelBroadcastService>();

// Add CORS if Angular runs on different port
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAngular", policy =>
    {
        policy.WithOrigins("http://localhost:4200") // Your Angular app URL
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials(); // Required for SignalR
    });
});

// Configure RabbitMQ options
builder.Services.Configure<RabbitMQOptions>(
    builder.Configuration.GetSection("RabbitMQOptions"));

// Configure SensorInfoServer HTTP client
var sensorInfoServerUrl = builder.Configuration["SensorInfoServer:BaseUrl"] ?? "http://localhost:5076";
builder.Services.AddHttpClient<ISensorInfoService, SensorInfoService>(client =>
{
    client.BaseAddress = new Uri(sensorInfoServerUrl);
    client.Timeout = TimeSpan.FromSeconds(30);
});

// Configure AuthService HTTP client (uses same SensorInfoServer)
builder.Services.AddHttpClient<IAuthService, AuthService>(client =>
{
    client.BaseAddress = new Uri(sensorInfoServerUrl);
    client.Timeout = TimeSpan.FromSeconds(30);
});

// Configure SensorDataServer HTTP client
var sensorDataServerUrl = builder.Configuration["SensorDataServer:BaseUrl"] ?? "http://localhost:5001";
builder.Services.AddHttpClient<IPanelReadingService, PanelReadingService>(client =>
{
    client.BaseAddress = new Uri(sensorDataServerUrl);
    client.Timeout = TimeSpan.FromSeconds(30);
});

await WaitForServicesHelper.WaitForSensorInfoAsync(new List<string> {  builder.Configuration.GetSection("SensorInfoServer")["BaseUrl"] }, CancellationToken.None);

var logger = builder.Services.BuildServiceProvider().GetRequiredService<ILogger<Program>>();
logger.LogInformation("SensorInfoServer is ready");

var app = builder.Build();

// Configure the HTTP request pipeline.
// CORS must be one of the first middleware in the pipeline
app.UseCors("AllowAngular");

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Map the SignalR hub
app.MapHub<PanelReadingsHub>("/panelReadingsHub");

app.Run();
