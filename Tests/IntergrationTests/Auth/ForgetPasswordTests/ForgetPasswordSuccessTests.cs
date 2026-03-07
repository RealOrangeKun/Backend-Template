using System.Net;
using Application.DTOs.Auth;
using Application.Utils;
using Tests.Common;
using TestsReusables.Auth;
using Xunit;

namespace Tests.Auth;

[Collection("Integration Tests")]
public class ForgetPasswordSuccessTests(CustomWebApplicationFactory factory) : BaseIntegrationTest(factory)
{
    [Fact]
    public async Task ForgetPassword_WithValidEmail_Returns200Ok_AndSendsEmail()
    {
        // Arrange
        var (_, _, _, email) = await AuthBackdoor.CreateVerifiedUserAsync("ForgetPassUser", "forget@example.com", "TestPassword123");

        var request = new ForgetPasswordRequestDto
        {
            Email = email
        };

        // Act
        var (response, content, _) = await ForgetPasswordTestHelpers.PostForgetPasswordAsync<SuccessApiResponse>(Client, request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(content);
        Assert.True(content.Success);

        // Verify email was sent (Hangfire dispatch is asynchronous, so poll briefly)
        var mailhogClient = Factory.MailhogClient!;
        var sentEmail = default(Tests.MailHog.MailhogMessage);

        for (var attempt = 0; attempt < 20; attempt++)
        {
            var messagesResponse = await mailhogClient.SearchMessagesByRecipientAsync(email);
            sentEmail = messagesResponse.Items.FirstOrDefault(m =>
                m.Content.Headers.TryGetValue("Subject", out var subject) &&
                subject.Any(s => s.Contains("Reset Your Password", StringComparison.OrdinalIgnoreCase)));

            if (sentEmail is not null)
            {
                break;
            }

            await Task.Delay(250);
        }

        Assert.NotNull(sentEmail);
    }
}
