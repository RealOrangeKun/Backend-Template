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
        // Arrange: Create a verified user with a refresh token
        var (userId, password, username, email) = await AuthBackdoor.CreateVerifiedUserAsync("RefreshLogicUser1", "refreshlogic1@example.com", "TestPassword123");
        await AuthBackdoor.AddRefreshTokenAsync(userId); // Create a valid token in DB

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
        // Arrange: Create a user and add an expired refresh token
        var (userId, password, username, email) = await AuthBackdoor.CreateVerifiedUserAsync("RefreshLogicUser2", "refreshlogic2@example.com", "TestPassword123");

        // Add a refresh token that's already expired
        var expiredRefreshToken = await AuthBackdoor.AddRefreshTokenAsync(userId, expiryTime: DateTime.UtcNow.AddDays(-1));

        var request = new RefreshTokenRequestDto
        {
            UserId = userId
        };

        // Act
        var (response, content, _, _) = await RefreshTokenTestHelpers.PostRefreshTokenAsync<FailApiResponse>(Client, request, expiredRefreshToken);

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
        // Arrange: Create two users with refresh tokens
        var (userId1, _, _, _) = await AuthBackdoor.CreateVerifiedUserAsync("RefreshLogicUser3", "refreshlogic3@example.com", "TestPassword123");
        var (userId2, _, _, _) = await AuthBackdoor.CreateVerifiedUserAsync("RefreshLogicUser4", "refreshlogic4@example.com", "TestPassword123");

        // Add refresh token for user1
        var user1RefreshToken = await AuthBackdoor.AddRefreshTokenAsync(userId1);
        await AuthBackdoor.AddRefreshTokenAsync(userId2); // User2 also needs a token in DB

        // Try to use user1's refresh token with user2's ID
        var request = new RefreshTokenRequestDto
        {
            UserId = userId2
        };

        // Act
        var (response, content, _, _) = await RefreshTokenTestHelpers.PostRefreshTokenAsync<FailApiResponse>(Client, request, user1RefreshToken);

        // Assert: Should fail because the refresh token doesn't match userId2
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotNull(content);
        Assert.False(content.Success);
        Assert.Contains("refresh token", content.Message.ToLower());
    }

    [Fact]
    public async Task RefreshToken_WithUnverifiedUser_StillSucceeds()
    {
        // Arrange: Create an unverified user (email not confirmed) with a refresh token
        var (userId, password, username, email) = await AuthBackdoor.CreateUnverifiedUserAsync("RefreshLogicUser5", "refreshlogic5@example.com", "TestPassword123");
        var refreshToken = await AuthBackdoor.AddRefreshTokenAsync(userId);

        var request = new RefreshTokenRequestDto
        {
            UserId = userId
        };

        // Act: Refresh token should still work even if email is not verified
        var (response, content, _, _) = await RefreshTokenTestHelpers.PostRefreshTokenAsync<SuccessApiResponse<RefreshTokenResponseDto>>(Client, request, refreshToken);

        // Assert: Should succeed - refresh tokens work regardless of email verification status
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(content);
        Assert.True(content.Success);
    }

    [Fact]
    public async Task RefreshToken_WithExternalAuthUser_StillSucceeds()
    {
        // Arrange: Create a user with external auth (e.g., Google) and add a refresh token
        var (userId, username, email) = await AuthBackdoor.CreateExternalAuthUserAsync("RefreshLogicUser6", "refreshlogic6@example.com");
        var refreshToken = await AuthBackdoor.AddRefreshTokenAsync(userId);

        var request = new RefreshTokenRequestDto
        {
            UserId = userId
        };

        // Act: Refresh token should work for external auth users too
        var (response, content, _, _) = await RefreshTokenTestHelpers.PostRefreshTokenAsync<SuccessApiResponse<RefreshTokenResponseDto>>(Client, request, refreshToken);

        // Assert: Should succeed - refresh tokens work for all auth types
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(content);
        Assert.True(content.Success);
    }
}
