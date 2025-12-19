using API.Hubs;
using API.Services;
using HubConnectorServer;
using Shared.Models;

var builder = WebApplication.CreateBuilder(args);

// Explicitly configure the URL to ensure it runs on port 5010
builder.WebHost.UseUrls("http://localhost:5010");

Console.WriteLine("ASPNETCORE_URLS = " + 
    Environment.GetEnvironmentVariable("ASPNETCORE_URLS"));

// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Add SignalR service
builder.Services.AddSignalR();
builder.Services.AddHostedService<KafkaConsumerService>();

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

// Configure Kafka options
builder.Services.Configure<KafkaOptions>(
    builder.Configuration.GetSection("KafkaOptions"));

// Configure SensorInfoServer HTTP client
var sensorInfoServerUrl = builder.Configuration["SensorInfoServer:BaseUrl"] ?? "http://localhost:5076";
builder.Services.AddHttpClient<ISensorInfoService, SensorInfoService>(client =>
{
    client.BaseAddress = new Uri(sensorInfoServerUrl);
    client.Timeout = TimeSpan.FromSeconds(30);
});

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
