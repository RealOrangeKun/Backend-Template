using System.Net;
using Application.DTOs.InternalAuth;
using Application.Utils;
using Tests.Common;
using Xunit;

namespace Tests.Auth;

public class LoginSuccessTests(CustomWebApplicationFactory factory) : BaseIntegrationTest(factory)
{
    [Fact]
    public async Task Login_WithValidUsername_Returns200OkWithToken()
    {
        var registerRequest = new RegisterRequestDto
        {
            Username = "LoginUser1",
            Email = "login1@example.com",
            Password = "TestPassword123"
        };
        await RegisterationTestHelpers.PostRegisterAsync<SuccessApiResponse<RegisterResponseDto>>(Client, registerRequest);

        var loginRequest = new LoginRequestDto
        {
            UsernameOrEmail = "LoginUser1",
            Password = "TestPassword123"
        };

        var (response, content, _) = await LoginTestHelpers.PostLoginAsync<SuccessApiResponse<LoginResponseDto>>(Client, loginRequest);

        AssertLoginSuccess(response, content);
    }

    [Fact]
    public async Task Login_WithValidEmail_Returns200OkWithToken()
    {
        // 1. Register a user
        var registerRequest = new RegisterRequestDto
        {
            Username = "LoginUser2",
            Email = "login2@example.com",
            Password = "TestPassword123"
        };
        await RegisterationTestHelpers.PostRegisterAsync<SuccessApiResponse<RegisterResponseDto>>(Client, registerRequest);

        // 2. Login
        var loginRequest = new LoginRequestDto
        {
            UsernameOrEmail = "login2@example.com",
            Password = "TestPassword123"
        };

        var (response, content, _) = await LoginTestHelpers.PostLoginAsync<SuccessApiResponse<LoginResponseDto>>(Client, loginRequest);

        AssertLoginSuccess(response, content);
    }

    private static void AssertLoginSuccess(HttpResponseMessage response, SuccessApiResponse<LoginResponseDto>? content)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(content);
        Assert.True(content.Success);
        Assert.Equal(200, content.StatusCode);
        Assert.Equal("Login successful.", content.Message);
        Assert.NotEqual(Guid.Empty, content.Data.UserId);
        Assert.False(string.IsNullOrWhiteSpace(content.Data.AccessToken));
        Assert.NotNull(content.TraceId);
    }
}
