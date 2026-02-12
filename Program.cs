using System.Text;
using Data.Mongo;
using Data.Sqlite;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using MongoDB.Driver;
using VanadiumAPI.Hubs;
using VanadiumAPI.Mqtt;
using VanadiumAPI.SensorDataSaver;
using VanadiumAPI.Services;
using VanadiumAPI.DTOs;
using Shared.Models;

var builder = WebApplication.CreateBuilder(args);

// MQTT
builder.Services.Configure<MqttOptions>(builder.Configuration.GetSection("MqttOptions"));
builder.Services.AddSingleton<MqttService>();
builder.Services.AddSingleton<IMqttService>(sp => sp.GetRequiredService<MqttService>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<MqttService>());

// SensorDataSaver (Channel-based, no RabbitMQ) - register as singleton first so it can be resolved for ISensorDataSaver and HostedService
builder.Services.AddSingleton<SensorDataSaver>();
builder.Services.AddSingleton<ISensorDataSaver>(sp => sp.GetRequiredService<SensorDataSaver>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<SensorDataSaver>());

// MongoDB
var mongoConnectionString = builder.Configuration.GetSection("Mongo")["ConnectionString"];
if (string.IsNullOrEmpty(mongoConnectionString))
    throw new InvalidOperationException("Mongo:ConnectionString is not configured.");
builder.Services.AddSingleton<IMongoClient>(new MongoClient(mongoConnectionString));
builder.Services.AddScoped<IPanelReadingRepository, PanelReadingRepository>();
builder.Services.AddScoped<MongoDbInitializer>();

// SQLite
builder.Services.AddDbContext<SqliteDataContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Sqlite") ?? "Data Source=app.db"));
builder.Services.AddScoped<IPanelInfoRepository, PanelInfoRepository>();

// JWT
var jwtSettings = new JwtSettings
{
    SecretKey = builder.Configuration["JwtSettings:SecretKey"] ?? "YourSuperSecretKeyThatShouldBeAtLeast32CharactersLong!",
    Issuer = builder.Configuration["JwtSettings:Issuer"] ?? "VanadiumAPI",
    Audience = builder.Configuration["JwtSettings:Audience"] ?? "VanadiumAPI",
    ExpirationMinutes = int.Parse(builder.Configuration["JwtSettings:ExpirationMinutes"] ?? "60")
};
builder.Services.Configure<JwtSettings>(options =>
{
    options.SecretKey = jwtSettings.SecretKey;
    options.Issuer = jwtSettings.Issuer;
    options.Audience = jwtSettings.Audience;
    options.ExpirationMinutes = jwtSettings.ExpirationMinutes;
});
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtSettings.Issuer,
        ValidAudience = jwtSettings.Audience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.SecretKey))
    };
});
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy => policy.RequireClaim("UserType", UserType.Admin.ToString()));
    options.AddPolicy("ManagerOrAdmin", policy => policy.RequireAssertion(context =>
        context.User.HasClaim("UserType", UserType.Admin.ToString()) ||
        context.User.HasClaim("UserType", UserType.Manager.ToString())));
});

// Services (in-process, no HTTP clients to other services)
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<ISensorInfoService, LocalSensorInfoService>();
builder.Services.AddScoped<IPanelReadingService, PanelReadingService>();
builder.Services.AddSingleton<IPanelBroadcastService, PanelBroadcastService>();

// SignalR
builder.Services.AddSignalR();

// Controllers + Swagger
builder.Services.AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "Vanadium API", Version = "v1" });
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme.",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        { new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } }, Array.Empty<string>() }
    });
});

// CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAngular", policy =>
    {
        policy.WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? new[] { "http://localhost:4200" })
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

// Optional URL from config
var serverUrl = builder.Configuration.GetSection("Server")["Url"];
if (!string.IsNullOrEmpty(serverUrl))
    builder.WebHost.UseUrls(serverUrl);

var app = builder.Build();

// Mongo init
using (var scope = app.Services.CreateScope())
{
    var initializer = scope.ServiceProvider.GetRequiredService<MongoDbInitializer>();
    await initializer.InitializeAsync();
}

app.UseCors("AllowAngular");

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<PanelReadingsHub>("/panelReadingsHub");

app.Run();
