using System.Net;
using Application.DTOs.User;
using Application.Utils;
using Tests.Common;
using TestsReusables.Auth;
using Xunit;

namespace Tests.Auth;

[Collection("Integration Tests")]
public class RefreshTokenSuccessTests(CustomWebApplicationFactory factory) : BaseIntegrationTest(factory)
{
    [Fact]
    public async Task RefreshToken_WithValidTokenAndCookie_Returns200OkWithNewTokens()
    {
        // Arrange: Create a verified user with a valid refresh token
        var (userId, password, username, email) = await AuthBackdoor.CreateVerifiedUserAsync("RefreshUser1", "refresh1@example.com", "TestPassword123");

        // Get the refresh token by querying the database directly
        var refreshToken = await GetUserRefreshTokenAsync(userId);

        var request = new RefreshTokenRequestDto
        {
            UserId = userId
        };

        // Act: Send refresh token request with cookie
        var (response, content, _, newRefreshTokenCookie) = await RefreshTokenTestHelpers.PostRefreshTokenAsync<SuccessApiResponse<RefreshTokenResponseDto>>(Client, request, refreshToken.ToString());

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(content);
        Assert.True(content.Success);
        Assert.Equal(200, content.StatusCode);
        Assert.Equal("Access token refreshed successfully.", content.Message);
        
        // Verify new tokens are returned
        Assert.NotNull(content.Data);
        Assert.False(string.IsNullOrWhiteSpace(content.Data.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(content.Data.RefreshToken));
        
        // Verify the refresh token is different from the old one
        Assert.NotEqual(refreshToken.ToString(), content.Data.RefreshToken);
        
        // Verify new refresh token cookie is set in response
        Assert.NotNull(newRefreshTokenCookie);
        Assert.Equal(content.Data.RefreshToken, newRefreshTokenCookie);
    }

    [Fact]
    public async Task RefreshToken_WithValidCookie_UpdatesRefreshTokenInDatabase()
    {
        // Arrange
        var (userId, password, username, email) = await AuthBackdoor.CreateVerifiedUserAsync("RefreshUser2", "refresh2@example.com", "TestPassword123");
        var oldRefreshToken = await GetUserRefreshTokenAsync(userId);

        var request = new RefreshTokenRequestDto
        {
            UserId = userId
        };

        // Act: First refresh
        var (response, content, _, newRefreshTokenCookie) = await RefreshTokenTestHelpers.PostRefreshTokenAsync<SuccessApiResponse<RefreshTokenResponseDto>>(Client, request, oldRefreshToken.ToString());

        // Assert: Success
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(content);
        
        // Verify database was updated
        var newRefreshTokenFromDb = await GetUserRefreshTokenAsync(userId);
        Assert.NotEqual(oldRefreshToken, newRefreshTokenFromDb);
        Assert.Equal(Guid.Parse(content!.Data.RefreshToken), newRefreshTokenFromDb);
    }

    [Fact]
    public async Task RefreshToken_CanUseNewRefreshToken_AfterFirstRefresh()
    {
        // Arrange
        var (userId, password, username, email) = await AuthBackdoor.CreateVerifiedUserAsync("RefreshUser3", "refresh3@example.com", "TestPassword123");
        var firstRefreshToken = await GetUserRefreshTokenAsync(userId);

        var request = new RefreshTokenRequestDto
        {
            UserId = userId
        };

        // Act: First refresh
        var (firstResponse, firstContent, _, firstNewCookie) = await RefreshTokenTestHelpers.PostRefreshTokenAsync<SuccessApiResponse<RefreshTokenResponseDto>>(Client, request, firstRefreshToken.ToString());
        
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.NotNull(firstNewCookie);

        // Act: Second refresh using the new refresh token from the first refresh
        var (secondResponse, secondContent, _, secondNewCookie) = await RefreshTokenTestHelpers.PostRefreshTokenAsync<SuccessApiResponse<RefreshTokenResponseDto>>(Client, request, firstNewCookie);

        // Assert: Both refreshes should succeed
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        Assert.NotNull(secondContent);
        Assert.True(secondContent.Success);
        
        // Verify we got different tokens each time
        Assert.NotEqual(firstRefreshToken.ToString(), firstContent!.Data.RefreshToken);
        Assert.NotEqual(firstContent.Data.RefreshToken, secondContent.Data.RefreshToken);
        Assert.NotEqual(firstNewCookie, secondNewCookie);
    }

    [Fact]
    public async Task RefreshToken_OldRefreshToken_CannotBeReused()
    {
        // Arrange
        var (userId, password, username, email) = await AuthBackdoor.CreateVerifiedUserAsync("RefreshUser4", "refresh4@example.com", "TestPassword123");
        var oldRefreshToken = await GetUserRefreshTokenAsync(userId);

        var request = new RefreshTokenRequestDto
        {
            UserId = userId
        };

        // Act: First refresh (invalidates old token)
        var (firstResponse, firstContent, _, _) = await RefreshTokenTestHelpers.PostRefreshTokenAsync<SuccessApiResponse<RefreshTokenResponseDto>>(Client, request, oldRefreshToken.ToString());
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);

        // Act: Try to use old refresh token again
        var (secondResponse, secondContent, _, _) = await RefreshTokenTestHelpers.PostRefreshTokenAsync<FailApiResponse>(Client, request, oldRefreshToken.ToString());

        // Assert: Should fail with invalid refresh token
        Assert.Equal(HttpStatusCode.Unauthorized, secondResponse.StatusCode);
        Assert.NotNull(secondContent);
        Assert.False(secondContent.Success);
        Assert.Contains("refresh token", secondContent.Message.ToLower());
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
}
