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

var app = builder.Build();

app.UseCors("AllowAngular");
app.MapHub<ClientHub>("/ioCloudApi");
app.MapGet("/", () => "Hello World!");

app.Run();

