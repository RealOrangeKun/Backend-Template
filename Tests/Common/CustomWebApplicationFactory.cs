using Infrastructure.Persistance;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;
using DotNet.Testcontainers.Containers;
using Xunit;
using Tests.MailHog;
using Respawn;
using Npgsql;
using System.Data.Common;

namespace Tests.Common;

public class CustomWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private PostgreSqlContainer? _dbContainer;
    private IContainer? _mailhogContainer;
    private RedisContainer? _redisContainer;
    
    public MailhogClient? MailhogClient { get; private set; }
    private Respawner? _respawner;
    private DbConnection? _dbConnection;

    /// <summary>
    /// Initializes the test environment and starts the required containers.
    /// </summary>
    public async Task InitializeAsync()
    {
        // hardcode SEQ_URL for testing purposes
        Environment.SetEnvironmentVariable("SEQ_URL", "http://localhost:5341");

        // Set up environment variables for Docker/Testcontainers
        TestEnvironment.Configure();
        await StartContainersAsync();

        // Set up Mailhog client for email assertions
        SetupMailhogClient();

        // Ensure database is migrated before Respawn initialization
        await EnsureDatabaseMigratedAsync();

        // Initialize Respawner
        await InitializeRespawnerAsync();
    }

    private async Task EnsureDatabaseMigratedAsync()
    {
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await dbContext.Database.MigrateAsync();
    }

    private async Task InitializeRespawnerAsync()
    {
        _dbConnection = new NpgsqlConnection(_dbContainer!.GetConnectionString());
        await _dbConnection.OpenAsync();
        _respawner = await Respawner.CreateAsync(_dbConnection, new RespawnerOptions
        {
            DbAdapter = DbAdapter.Postgres,
            SchemasToInclude = ["public"]
        });
    }

    public async Task ResetDatabaseAsync()
    {
        await _respawner!.ResetAsync(_dbConnection!);
    }

    public async Task ResetRedisAsync()
    {
        if (_redisContainer != null && _redisContainer.State == TestcontainersStates.Running)
        {
            await _redisContainer.ExecAsync(new[] { "redis-cli", "FLUSHALL" });
        }
    }

    /// <summary>
    /// Creates and starts the PostgreSQL and Mailhog containers using the ContainerFactory.
    /// </summary>
    private async Task StartContainersAsync()
    {
        _dbContainer = ContainerFactory.CreatePostgreSqlContainer();
        _mailhogContainer = ContainerFactory.CreateMailhogContainer();
        _redisContainer = ContainerFactory.CreateRedisContainer();

        await Task.WhenAll(
            _dbContainer.StartAsync(),
            _mailhogContainer.StartAsync(),
            _redisContainer.StartAsync()
        );
    }

    /// <summary>
    /// Initializes the MailhogClient using the mapped HTTP port from the Mailhog container.
    /// </summary>
    private void SetupMailhogClient()
    {
        if (_mailhogContainer == null) return;
        var mailhogHttpPort = _mailhogContainer.GetMappedPublicPort(8025);
        MailhogClient = new MailhogClient($"http://localhost:{mailhogHttpPort}");
    }

    public new async Task DisposeAsync()
    {
        if (_dbConnection != null) await _dbConnection.DisposeAsync();
        var tasks = new List<Task>();
        if (_mailhogContainer != null) tasks.Add(_mailhogContainer.DisposeAsync().AsTask());
        if (_dbContainer != null) tasks.Add(_dbContainer.DisposeAsync().AsTask());
        if (_redisContainer != null) tasks.Add(_redisContainer.DisposeAsync().AsTask());
        await Task.WhenAll(tasks);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ValidateContainers();
        ConfigureEmailSettings();
        ConfigureDatabaseSettings();
        ConfigureRedisSettings();

        builder.ConfigureTestServices(services =>
        {
            ReplaceDbContext(services);
        });
    }

    private void ValidateContainers()
    {
        if (_dbContainer == null || _mailhogContainer == null || _redisContainer == null)
        {
            throw new InvalidOperationException("Containers not initialized.");
        }
    }

    private void ConfigureEmailSettings()
    {
        if (_mailhogContainer == null) return;
        var smtpPort = _mailhogContainer.GetMappedPublicPort(1025);
        TestEnvironment.SetEmailEnvironmentVariables(smtpPort);
    }

    private void ConfigureDatabaseSettings()
    {
        if (_dbContainer == null) return;
        TestEnvironment.SetDatabaseEnvironmentVariables(_dbContainer.GetConnectionString());
    }

    private void ConfigureRedisSettings()
    {
        if (_redisContainer == null) return;
        Environment.SetEnvironmentVariable("REDIS_CONNECTION_STRING", _redisContainer.GetConnectionString());
    }

    private void ReplaceDbContext(IServiceCollection services)
    {
        var descriptor = services.SingleOrDefault(
            d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
        if (descriptor != null)
        {
            services.Remove(descriptor);
        }
        services.AddDbContext<AppDbContext>(options =>
        {
            options.UseNpgsql(_dbContainer!.GetConnectionString())
                   .UseSnakeCaseNamingConvention()
                   .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));
        });
    }
}