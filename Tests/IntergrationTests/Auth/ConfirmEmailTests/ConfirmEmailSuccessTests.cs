using System.Net;
using Application.DTOs.Auth;
using Application.Utils;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Tests.Common;
using TestsReusables.Auth;
using Xunit;

namespace Tests.Auth;

[Collection("Integration Tests")]
public class ConfirmEmailSuccessTests(CustomWebApplicationFactory factory) : BaseIntegrationTest(factory)
{
    [Fact]
    public async Task ConfirmEmail_WithValidToken_Returns200OKAndRemovesTokenFromRedis()
    {
        // Arrange: create an unverified user directly in the DB and seed a confirmation token into Redis
        var (userIdGuid, password, username, email) = await AuthBackdoor.CreateUnverifiedUserAsync("ConfirmUser", "confirm@example.com", "TestPassword123");
        var token = Guid.NewGuid().ToString();
        // Seed token into Redis using IDistributedCache via Backdoor
        await AuthBackdoor.SeedConfirmationTokenAsync(Factory, userIdGuid, token);

        var confirmRequest = new ConfirmEmailRequestDto
        {
            Token = token
        };

        var (confirmResponse, confirmContent, _) = await ConfirmEmailTestHelpers.PostConfirmEmailAsync<SuccessApiResponse<ConfirmEmailResponseDto>>(Client, confirmRequest);

        Assert.Equal(HttpStatusCode.OK, confirmResponse.StatusCode);
        Assert.True(confirmContent!.Success);
        Assert.Equal("Email confirmation successful.", confirmContent.Message);

        // Assert deviceId cookie is set
        var cookies = confirmResponse.Headers.GetValues("Set-Cookie");
        Assert.Contains(cookies, c => c.StartsWith("deviceId="));

        // Verify token is removed from Redis
        using var scope = Factory.Services.CreateScope();
        var cache = scope.ServiceProvider.GetRequiredService<IDistributedCache>();
        var key = $"new_user:{token}";
        var tokenAfterConfirmation = await cache.GetStringAsync(key);
        Assert.Null(tokenAfterConfirmation);
    }

    [Fact]
    public async Task ConfirmEmail_WithValidToken_AddsDeviceIdToDatabase()
    {
        // Arrange: create an unverified user and seed a confirmation token
        var (userId, password, username, email) = await AuthBackdoor.CreateUnverifiedUserAsync("DeviceConfirmUser", "deviceconfirm@example.com", "TestPassword123");
        var token = Guid.NewGuid().ToString();
        await AuthBackdoor.SeedConfirmationTokenAsync(Factory, userId, token);

        // Set a device ID cookie
        var deviceId = Guid.NewGuid();
        Client.DefaultRequestHeaders.Add("X-Test-Device-ID", deviceId.ToString());

        var confirmRequest = new ConfirmEmailRequestDto
        {
            Token = token
        };

        // Act
        var (confirmResponse, confirmContent, _) = await ConfirmEmailTestHelpers.PostConfirmEmailAsync<SuccessApiResponse<ConfirmEmailResponseDto>>(Client, confirmRequest);

        // Assert
        Assert.Equal(HttpStatusCode.OK, confirmResponse.StatusCode);
        Assert.True(confirmContent!.Success);

        // Assert deviceId cookie is set
        var cookies = confirmResponse.Headers.GetValues("Set-Cookie");
        Assert.Contains(cookies, c => c.Contains($"deviceId={deviceId}"));

        // Verify device was added to database
        var connStr = Environment.GetEnvironmentVariable("CONNECTION_STRING");
        await using var conn = new Npgsql.NpgsqlConnection(connStr);
        await conn.OpenAsync();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM user_devices WHERE user_id = @userId AND device_id = @deviceId";
        cmd.Parameters.AddWithValue("@userId", userId);
        cmd.Parameters.AddWithValue("@deviceId", deviceId);
        var count = (long)(await cmd.ExecuteScalarAsync())!;
        Assert.Equal(1, count);
    }
}
