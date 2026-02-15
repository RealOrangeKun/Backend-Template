using System.Net.Http.Json;
using System.Text.Json;
using Application.DTOs.User;

namespace Tests.Auth;

public static class RefreshTokenTestHelpers
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static async Task<(HttpResponseMessage Response, TResponse? Content, string Json, string? RefreshTokenCookie)>
        PostRefreshTokenAsync<TResponse>(HttpClient client, RefreshTokenRequestDto request, string? refreshTokenCookie = null)
    {
        var requestMessage = new HttpRequestMessage(HttpMethod.Post, "/api/v1/internal-auth/refresh-token")
        {
            Content = JsonContent.Create(request)
        };

        // Add refresh token cookie if provided
        if (!string.IsNullOrEmpty(refreshTokenCookie))
        {
            requestMessage.Headers.Add("Cookie", $"refreshToken={refreshTokenCookie}");
        }

        var response = await client.SendAsync(requestMessage);
        var json = await response.Content.ReadAsStringAsync();
        var content = JsonSerializer.Deserialize<TResponse>(json, JsonOptions);

        // Extract the refresh token cookie from the response
        string? newRefreshTokenCookie = null;
        if (response.Headers.TryGetValues("Set-Cookie", out var cookies))
        {
            var refreshTokenCookieHeader = cookies.FirstOrDefault(c => c.StartsWith("refreshToken="));
            if (refreshTokenCookieHeader != null)
            {
                // Extract just the value (before the first semicolon)
                var cookieValue = refreshTokenCookieHeader.Split(';')[0].Replace("refreshToken=", "");
                newRefreshTokenCookie = cookieValue;
            }
        }

        return (response, content, json, newRefreshTokenCookie);
    }
}
