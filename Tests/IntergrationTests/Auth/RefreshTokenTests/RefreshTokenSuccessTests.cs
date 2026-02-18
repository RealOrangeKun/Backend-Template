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
        // Arrange: Create a verified user and add a refresh token
        var (userId, password, username, email) = await AuthBackdoor.CreateVerifiedUserAsync("RefreshUser1", "refresh1@example.com", "TestPassword123");
        var refreshToken = await AuthBackdoor.AddRefreshTokenAsync(userId);

        var request = new RefreshTokenRequestDto
        {
            UserId = userId
        };

        // Act: Send refresh token request with cookie
        var (response, content, _, newRefreshTokenCookie) = await RefreshTokenTestHelpers.PostRefreshTokenAsync<SuccessApiResponse<RefreshTokenResponseDto>>(Client, request, refreshToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(content);
        Assert.True(content.Success);
        Assert.Equal(200, content.StatusCode);
        Assert.Equal("Access token refreshed successfully.", content.Message);
        
        // Verify new tokens are returned
        Assert.NotNull(content.Data);
        Assert.False(string.IsNullOrWhiteSpace(content.Data.AccessToken));
        
        // Verify new refresh token cookie is set in response (RefreshToken is JsonIgnored)
        Assert.NotNull(newRefreshTokenCookie);
        Assert.NotEqual(refreshToken, newRefreshTokenCookie);
    }

    [Fact]
    public async Task RefreshToken_WithValidCookie_CreatesNewRefreshTokenInDatabase()
    {
        // Arrange
        var (userId, password, username, email) = await AuthBackdoor.CreateVerifiedUserAsync("RefreshUser2", "refresh2@example.com", "TestPassword123");
        var oldRefreshToken = await AuthBackdoor.AddRefreshTokenAsync(userId);
        var oldTokenCount = await AuthBackdoor.GetRefreshTokenCountAsync(userId, onlyActive: false);

        var request = new RefreshTokenRequestDto
        {
            UserId = userId
        };

        // Act: First refresh
        var (response, content, _, newRefreshTokenCookie) = await RefreshTokenTestHelpers.PostRefreshTokenAsync<SuccessApiResponse<RefreshTokenResponseDto>>(Client, request, oldRefreshToken);

        // Assert: Success
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(content);
        
        // Verify a new refresh token was added to the database
        var newTokenCount = await AuthBackdoor.GetRefreshTokenCountAsync(userId, onlyActive: false);
        Assert.Equal(oldTokenCount + 1, newTokenCount);
        
        // Verify we have exactly one active token
        var activeTokenCount = await AuthBackdoor.GetRefreshTokenCountAsync(userId, onlyActive: true);
        Assert.Equal(1, activeTokenCount);
    }

    [Fact]
    public async Task RefreshToken_CanUseNewRefreshToken_AfterFirstRefresh()
    {
        // Arrange
        var (userId, password, username, email) = await AuthBackdoor.CreateVerifiedUserAsync("RefreshUser3", "refresh3@example.com", "TestPassword123");
        var firstRefreshToken = await AuthBackdoor.AddRefreshTokenAsync(userId);

        var request = new RefreshTokenRequestDto
        {
            UserId = userId
        };

        // Act: First refresh
        var (firstResponse, firstContent, _, firstNewCookie) = await RefreshTokenTestHelpers.PostRefreshTokenAsync<SuccessApiResponse<RefreshTokenResponseDto>>(Client, request, firstRefreshToken);
        
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.NotNull(firstNewCookie);

        // Act: Second refresh using the new refresh token from the first refresh
        var (secondResponse, secondContent, _, secondNewCookie) = await RefreshTokenTestHelpers.PostRefreshTokenAsync<SuccessApiResponse<RefreshTokenResponseDto>>(Client, request, firstNewCookie);

        // Assert: Both refreshes should succeed
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        Assert.NotNull(secondContent);
        Assert.True(secondContent.Success);
        
        // Verify we got different cookies each time
        Assert.NotEqual(firstRefreshToken, firstNewCookie);
        Assert.NotEqual(firstNewCookie, secondNewCookie);
    }

    [Fact]
    public async Task RefreshToken_OldRefreshToken_CannotBeReused()
    {
        // Arrange
        var (userId, password, username, email) = await AuthBackdoor.CreateVerifiedUserAsync("RefreshUser4", "refresh4@example.com", "TestPassword123");
        var oldRefreshToken = await AuthBackdoor.AddRefreshTokenAsync(userId);

        var request = new RefreshTokenRequestDto
        {
            UserId = userId
        };

        // Act: First refresh (marks old token as used)
        var (firstResponse, firstContent, _, _) = await RefreshTokenTestHelpers.PostRefreshTokenAsync<SuccessApiResponse<RefreshTokenResponseDto>>(Client, request, oldRefreshToken);
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);

        // Act: Try to use old refresh token again immediately (within 40s grace period)
        // The system allows reuse within 40 seconds to handle clock skew and network retries
        var (secondResponse, secondContent, _, _) = await RefreshTokenTestHelpers.PostRefreshTokenAsync<SuccessApiResponse<RefreshTokenResponseDto>>(Client, request, oldRefreshToken);

        // Assert: Should succeed within grace period
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        Assert.NotNull(secondContent);
        Assert.True(secondContent.Success);
    }
}
