using System.Net;
using Application.DTOs.InternalAuth;
using Application.Utils;
using Tests.Common;
using Xunit;

namespace Tests.Auth;

public class RegisterationSuccessTests(CustomWebApplicationFactory factory) : BaseIntegrationTest(factory)
{
    [Fact]
    public async Task Register_WithAddresss_Returns201CreatedWithUserId()
    {
        var request = new RegisterRequestDto
        {
            Username = "ValidUser123",
            Email = "user@example.com",
            Password = "TestPassword123",
            Address = "123 Main Street"
        };

        var (response, content, _) = await RegisterationTestHelpers.PostRegisterAsync<SuccessApiResponse<RegisterResponseDto>>(Client, request);

        AssertRegistrationSuccess(response, content);
    }

    [Fact]
    public async Task Register_WithPhoneNumber_Returns201CreatedWithUserId()
    {
        var request = new RegisterRequestDto
        {
            Username = "ValidUser123",
            Email = "user@example.com",
            Password = "TestPassword123",
            PhoneNumber = "+1234567890",
        };

        var (response, content, _) = await RegisterationTestHelpers.PostRegisterAsync<SuccessApiResponse<RegisterResponseDto>>(Client, request);

        AssertRegistrationSuccess(response, content);
    }

    [Fact]
    public async Task Register_WithMinimalValidData_Returns201CreatedWithUserIdAsync()
    {
        var request = new RegisterRequestDto
        {
            Username = "MinimalUser",
            Email = "minimal@example.com",
            Password = "MinimalPass123"
        };

        var (response, content, _) = await RegisterationTestHelpers.PostRegisterAsync<SuccessApiResponse<RegisterResponseDto>>(Client, request);

        AssertRegistrationSuccess(response, content);
    }

    private static void AssertRegistrationSuccess(HttpResponseMessage response, SuccessApiResponse<RegisterResponseDto>? content)
    {
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(content);
        Assert.True(content.Success);
        Assert.Equal(201, content.StatusCode);
        Assert.Equal("Registration successful.", content.Message);
        Assert.NotEqual(Guid.Empty, content.Data.UserId);
        Assert.NotNull(content.TraceId);
    }
}
