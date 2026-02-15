using System.Net;
using Application.DTOs.User;
using Application.Utils;
using Tests.Common;
using TestsReusables.Auth;
using Xunit;

namespace Tests.Auth;

[Collection("Integration Tests")]
public class RefreshTokenLogicTests(CustomWebApplicationFactory factory) : BaseIntegrationTest(factory)
{
    [Fact]
    public async Task RefreshToken_WithNonExistentUser_Returns404NotFound()
    {
        var nonExistentUserId = Guid.NewGuid();
        var request = new RefreshTokenRequestDto
        {
            UserId = nonExistentUserId
        };

        var dummyRefreshToken = Guid.NewGuid().ToString();

        var (response, content, _, _) = await RefreshTokenTestHelpers.PostRefreshTokenAsync<FailApiResponse>(Client, request, dummyRefreshToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.NotNull(content);
        Assert.False(content.Success);
        Assert.Equal(404, content.StatusCode);
        Assert.Contains("user", content.Message.ToLower());
    }

    [Fact]
    public async Task RefreshToken_WithInvalidRefreshToken_Returns401Unauthorized()
    {
        // Arrange: Create a verified user
        var (userId, password, username, email) = await AuthBackdoor.CreateVerifiedUserAsync("RefreshLogicUser1", "refreshlogic1@example.com", "TestPassword123");

        var request = new RefreshTokenRequestDto
        {
            UserId = userId
        };

        // Use a different refresh token than what's stored in the database
        var invalidRefreshToken = Guid.NewGuid().ToString();

        // Act
        var (response, content, _, _) = await RefreshTokenTestHelpers.PostRefreshTokenAsync<FailApiResponse>(Client, request, invalidRefreshToken);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotNull(content);
        Assert.False(content.Success);
        Assert.Equal(401, content.StatusCode);
        Assert.Contains("refresh token", content.Message.ToLower());
    }

    [Fact]
    public async Task RefreshToken_WithExpiredRefreshToken_Returns401Unauthorized()
    {
        // Arrange: Create a user and let some time pass, then set expiry to past
        var (userId, password, username, email) = await AuthBackdoor.CreateVerifiedUserAsync("RefreshLogicUser2", "refreshlogic2@example.com", "TestPassword123");

        // Get the current refresh token first
        var refreshToken = await GetUserRefreshTokenAsync(userId);
        
        // Then manually set the refresh token expiry to the past
        await SetUserRefreshTokenExpiryAsync(userId, DateTime.UtcNow.AddDays(-1));

        var request = new RefreshTokenRequestDto
        {
            UserId = userId
        };

        // Act
        var (response, content, _, _) = await RefreshTokenTestHelpers.PostRefreshTokenAsync<FailApiResponse>(Client, request, refreshToken.ToString());

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotNull(content);
        Assert.False(content.Success);
        Assert.Equal(401, content.StatusCode);
        Assert.Contains("refresh token", content.Message.ToLower());
    }

    [Fact]
    public async Task RefreshToken_WithMismatchedUserId_Returns401Unauthorized()
    {
        // Arrange: Create two users
        var (userId1, _, _, _) = await AuthBackdoor.CreateVerifiedUserAsync("RefreshLogicUser3", "refreshlogic3@example.com", "TestPassword123");
        var (userId2, _, _, _) = await AuthBackdoor.CreateVerifiedUserAsync("RefreshLogicUser4", "refreshlogic4@example.com", "TestPassword123");

        // Get the refresh token for user1
        var user1RefreshToken = await GetUserRefreshTokenAsync(userId1);

        // Try to use user1's refresh token with user2's ID
        var request = new RefreshTokenRequestDto
        {
            UserId = userId2
        };

        // Act
        var (response, content, _, _) = await RefreshTokenTestHelpers.PostRefreshTokenAsync<FailApiResponse>(Client, request, user1RefreshToken.ToString());

        // Assert: Should fail because the refresh token doesn't match userId2
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotNull(content);
        Assert.False(content.Success);
        Assert.Contains("refresh token", content.Message.ToLower());
    }

    [Fact]
    public async Task RefreshToken_WithUnverifiedUser_StillSucceeds()
    {
        // Arrange: Create an unverified user (email not confirmed)
        var (userId, password, username, email) = await AuthBackdoor.CreateUnverifiedUserAsync("RefreshLogicUser5", "refreshlogic5@example.com", "TestPassword123");
        
        var refreshToken = await GetUserRefreshTokenAsync(userId);

        var request = new RefreshTokenRequestDto
        {
            UserId = userId
        };

        // Act: Refresh token should still work even if email is not verified
        var (response, content, _, _) = await RefreshTokenTestHelpers.PostRefreshTokenAsync<SuccessApiResponse<RefreshTokenResponseDto>>(Client, request, refreshToken.ToString());

        // Assert: Should succeed - refresh tokens work regardless of email verification status
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(content);
        Assert.True(content.Success);
    }

    [Fact]
    public async Task RefreshToken_WithExternalAuthUser_StillSucceeds()
    {
        // Arrange: Create a user with external auth scheme (e.g., Google)
        var (userId, _, username, _) = await AuthBackdoor.CreateVerifiedUserAsync("RefreshLogicUser6", "refreshlogic6@example.com", "TestPassword123", authScheme: 1);
        
        var refreshToken = await GetUserRefreshTokenAsync(userId);

        var request = new RefreshTokenRequestDto
        {
            UserId = userId
        };

        // Act: Refresh token should work for external auth users too
        var (response, content, _, _) = await RefreshTokenTestHelpers.PostRefreshTokenAsync<SuccessApiResponse<RefreshTokenResponseDto>>(Client, request, refreshToken.ToString());

        // Assert: Should succeed - refresh tokens work for all auth schemes
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(content);
        Assert.True(content.Success);
    }

    /// <summary>
    /// Helper method to retrieve the current refresh token for a user from the database
    /// </summary>
    private async Task<Guid> GetUserRefreshTokenAsync(Guid userId)
    {
        var connStr = Environment.GetEnvironmentVariable("CONNECTION_STRING");
        if (string.IsNullOrEmpty(connStr)) throw new InvalidOperationException("CONNECTION_STRING environment variable is not set.");

        await using var conn = new Npgsql.NpgsqlConnection(connStr);
        await conn.OpenAsync();

        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT refresh_token FROM users WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", userId);

        var result = await cmd.ExecuteScalarAsync();
        return result != null ? (Guid)result : Guid.Empty;
    }

    /// <summary>
    /// Helper method to set the refresh token expiry time for a user in the database
    /// </summary>
    private async Task SetUserRefreshTokenExpiryAsync(Guid userId, DateTime expiryTime)
    {
        var connStr = Environment.GetEnvironmentVariable("CONNECTION_STRING");
        if (string.IsNullOrEmpty(connStr)) throw new InvalidOperationException("CONNECTION_STRING environment variable is not set.");

        await using var conn = new Npgsql.NpgsqlConnection(connStr);
        await conn.OpenAsync();

        var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE users SET refresh_token_expiry_time = @expiry WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", userId);
        cmd.Parameters.AddWithValue("@expiry", expiryTime);

        await cmd.ExecuteNonQueryAsync();
    }
}
