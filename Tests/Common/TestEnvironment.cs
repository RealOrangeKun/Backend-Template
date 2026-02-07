namespace Tests.Common;

/// <summary>
/// Handles global environment configuration for integration tests.
/// </summary>
public static class TestEnvironment
{
    /// <summary>
    /// Sets up environment variables so Testcontainers can connect to Docker/Podman.
    /// </summary>
    public static void Configure()
    {
        // Configure Testcontainers for Docker environment
        // On Linux, the default Docker socket is /var/run/docker.sock
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DOCKER_HOST")))
        {
            Environment.SetEnvironmentVariable("DOCKER_HOST", "unix:///var/run/docker.sock");
        }
        // Disables Ryuk (resource reaper) to avoid timeout issues
        Environment.SetEnvironmentVariable("TESTCONTAINERS_RYUK_DISABLED", "true");
    }

    /// <summary>
    /// Sets environment variables so the app uses the test database.
    /// </summary>
    public static void SetDatabaseEnvironmentVariables(string connectionString)
    {
        Environment.SetEnvironmentVariable("CONNECTION_STRING", connectionString);
    }

    /// <summary>
    /// Sets environment variables so the app uses Mailhog for email delivery during tests.
    /// </summary>
    public static void SetEmailEnvironmentVariables(int mailhogSmtpPort)
    {
        Environment.SetEnvironmentVariable("EMAIL_HOST", "localhost");
        Environment.SetEnvironmentVariable("EMAIL_PORT", mailhogSmtpPort.ToString());
        Environment.SetEnvironmentVariable("EMAIL_USERNAME", "test");
        Environment.SetEnvironmentVariable("EMAIL_PASSWORD", "test");
        Environment.SetEnvironmentVariable("EMAIL_FROM", "noreply@test.com");
        Environment.SetEnvironmentVariable("EMAIL_ENABLE_SSL", "false");
    }
}
