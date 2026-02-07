using Infrastructure.Persistance;
using Microsoft.EntityFrameworkCore;
using MyBackendTemplate.API.Middlewares;
using DotNetEnv;
using Application.Services;
using System.Net.Mail;
using System.Net;
using Application.Interfaces;
using Serilog;

Env.Load("../../.env");  // Load .env from root

var builder = WebApplication.CreateBuilder(args);

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File("Logs/log-.txt", rollingInterval: RollingInterval.Day)
    .WriteTo.Seq(Environment.GetEnvironmentVariable("SEQ_URL") ?? 
        throw new InvalidOperationException("SEQ_URL environment variable is not set."))
    .CreateLogger();

builder.Host.UseSerilog();

try
{
    Log.Information("Starting web host");

    builder.Services.AddControllers();

    // Get connection string from environment variable with fallback for migrations
    var connectionString = Environment.GetEnvironmentVariable("CONNECTION_STRING") ?? 
        throw new InvalidOperationException("CONNECTION_STRING environment variable is not set.");

    // naming convention for postgreSQL is snake_case
    builder.Services.AddDbContext<AppDbContext>(options =>
        options.UseNpgsql(connectionString)
               .UseSnakeCaseNamingConvention());

    // 1. Email SMTP Configuration
    var emailConfig = new Dictionary<string, string>
    {
        { "Host", Environment.GetEnvironmentVariable("EMAIL_HOST") ?? 
            throw new InvalidOperationException("EMAIL_HOST environment variable is not set.") },
        { "Port", Environment.GetEnvironmentVariable("EMAIL_PORT") ?? 
            throw new InvalidOperationException("EMAIL_PORT environment variable is not set.") },
        { "Username", Environment.GetEnvironmentVariable("EMAIL_USERNAME") ?? 
            throw new InvalidOperationException("EMAIL_USERNAME environment variable is not set.") },
        { "Password", Environment.GetEnvironmentVariable("EMAIL_PASSWORD") ?? 
            throw new InvalidOperationException("EMAIL_PASSWORD environment variable is not set.") },
        { "From", Environment.GetEnvironmentVariable("EMAIL_FROM") ?? 
            throw new InvalidOperationException("EMAIL_FROM environment variable is not set.") }
    };

    var smtpClient = new SmtpClient
    {
        Host = emailConfig["Host"],
        Port = int.Parse(emailConfig["Port"]),
        EnableSsl = true,
        DeliveryMethod = SmtpDeliveryMethod.Network,
        UseDefaultCredentials = false,
        Credentials = new NetworkCredential(
            emailConfig["Username"], 
            emailConfig["Password"]
        )
    };

    // 3. Register FluentEmail with these settings
    builder.Services
        .AddFluentEmail(emailConfig["From"]) // The default sender
        .AddSmtpSender(smtpClient);
    builder.Services.AddScoped<IEmailService, EmailService>();

    // 4. Redis Caching Configuration
    var redisConnectionString = Environment.GetEnvironmentVariable("REDIS_CONNECTION_STRING") ?? 
        throw new InvalidOperationException("REDIS_CONNECTION_STRING environment variable is not set.");

    builder.Services.AddStackExchangeRedisCache(options =>
    {
        options.Configuration = redisConnectionString;
        options.InstanceName = "MyBackendTemplate_";
    });

    // Register application services
    builder.Services.AddScoped<AuthService>();

    var app = builder.Build();

    app.UseSerilogRequestLogging();

    app.UseMiddleware<GlobalExceptionHandlerMiddleware>();

    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi();
    }

    app.MapControllers();

    // make a healthcheck endpoint
    app.MapGet("/health", () => Results.Ok(new { status = "Healthy" }));

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Host terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

public partial class Program { } // required for tests