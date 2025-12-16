using API.Hubs;
using HubConnectorServer;
using Shared.Models;

var builder = WebApplication.CreateBuilder(args);

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

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
// Use CORS before mapping hub
app.UseCors("AllowAngular");

// Map the SignalR hub
app.MapHub<PanelReadingsHub>("/panelReadingsHub");

app.Run();
