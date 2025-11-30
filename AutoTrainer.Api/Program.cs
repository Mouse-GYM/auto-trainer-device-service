using AutoTrainer.Api.CommandQueue;
using AutoTrainer.Api.MessageQueue;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSignalR();

builder.Services.AddHostedService<MessageSubscriptionWorker>();
builder.Services.AddHostedService<CommandQueueWorker>();

builder.Logging.AddSimpleConsole(options =>
{
    options.SingleLine = true;
});

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(
        builder =>
        {
            builder
                .AllowAnyOrigin()
                .AllowAnyHeader()
                .AllowAnyMethod();
        });
});

// Add services to the container.

var app = builder.Build();

app.UseCors();

// Configure the HTTP request pipeline.

var summaries = new[]
{
    "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
};

app.MapGet("/weatherforecast", () =>
{
    var forecast = Enumerable.Range(1, 5).Select(index =>
        new WeatherForecast
        (
            DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
            Random.Shared.Next(-20, 55),
            summaries[Random.Shared.Next(summaries.Length)]
        ))
        .ToArray();
    return forecast;
});


app.MapHub<MessageHub>("/messages");

app.Run();

internal record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
