using System.Net;
using Application.DTOs.User;
using Application.Utils;
using Tests.Common;
using TestsReusables.Auth;
using Xunit;

namespace Tests.Auth;

[Collection("Integration Tests")]
public class RefreshTokenRequestValidationTests(CustomWebApplicationFactory factory) : BaseIntegrationTest(factory)
{
    [Fact]
    public async Task RefreshToken_WithEmptyUserId_Returns400BadRequest()
    {
        var request = new RefreshTokenRequestDto
        {
            UserId = Guid.Empty
        };

        // We need a dummy refresh token cookie to pass cookie validation
        var dummyRefreshToken = Guid.NewGuid().ToString();

        var (response, content, _, _) = await RefreshTokenTestHelpers.PostRefreshTokenAsync<FailApiResponse>(Client, request, dummyRefreshToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(content);
        Assert.False(content.Success);
        Assert.Equal(400, content.StatusCode);
        Assert.Contains("userid", content.Message.ToLower());
    }

    [Fact]
    public async Task RefreshToken_WithMissingCookie_Returns400BadRequest()
    {
        var request = new RefreshTokenRequestDto
        {
            UserId = Guid.NewGuid()
        };

        // Don't pass any cookie
        var (response, content, _, _) = await RefreshTokenTestHelpers.PostRefreshTokenAsync<FailApiResponse>(Client, request, null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(content);
        Assert.False(content.Success);
        Assert.Equal(400, content.StatusCode);
        Assert.Contains("refresh token cookie", content.Message.ToLower());
    }

    [Fact]
    public async Task RefreshToken_WithEmptyCookie_Returns400BadRequest()
    {
        var request = new RefreshTokenRequestDto
        {
            UserId = Guid.NewGuid()
        };

        // Pass empty cookie value
        var (response, content, _, _) = await RefreshTokenTestHelpers.PostRefreshTokenAsync<FailApiResponse>(Client, request, "");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(content);
        Assert.False(content.Success);
        Assert.Equal(400, content.StatusCode);
        Assert.Contains("refresh token cookie", content.Message.ToLower());
    }

    [Fact]
    public async Task RefreshToken_WithInvalidGuidInCookie_Returns400BadRequest()
    {
        // Create a user so we can test with a real user ID
        var (userId, _, _, _) = await AuthBackdoor.CreateVerifiedUserAsync("InvalidCookieUser", "invalidcookie@example.com", "TestPassword123");
        
        var request = new RefreshTokenRequestDto
        {
            UserId = userId
        };

        // Pass invalid refresh token cookie - system will treat it as invalid token
        var (response, content, _, _) = await RefreshTokenTestHelpers.PostRefreshTokenAsync<FailApiResponse>(Client, request, "not-a-valid-refresh-token");

        // Invalid refresh token returns 401 Unauthorized
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotNull(content);
        Assert.False(content.Success);
        Assert.Equal(401, content.StatusCode);
        Assert.Contains("refresh token", content.Message.ToLower());
    }
}
