using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Application.DTOs.Auth;

namespace Tests.Auth;

public static class RegisterationTestHelpers
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static async Task<(HttpResponseMessage Response, TResponse? Content, string Json)>
        PostRegisterAsync<TResponse>(HttpClient client, RegisterRequestDto request, string? idempotencyKey = "AUTO")
    {
        var message = new HttpRequestMessage(HttpMethod.Post, "/api/v1/internal-auth/register")
        {
            Content = JsonContent.Create(request)
        };

        if (idempotencyKey == "AUTO")
        {
            message.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        }
        else if (idempotencyKey != null)
        {
            message.Headers.Add("Idempotency-Key", idempotencyKey);
        }

        var response = await client.SendAsync(message);
        var json = await response.Content.ReadAsStringAsync();
        var content = JsonSerializer.Deserialize<TResponse>(json, JsonOptions);
        return (response, content, json);
    }

    public static string? ExtractTokenFromBody(string body)
    {
        // Matches a 6-digit code. Try to find anywhere in the body.
        var match = Regex.Match(body, @"\d{6}");
        if (match.Success) return match.Value;

        // If not found, it might be base64 encoded (common in Mailhog for some reason)
        try
        {
            var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(body));
            match = Regex.Match(decoded, @"\d{6}");
            if (match.Success) return match.Value;
        }
        catch { /* Not base64 */ }

        return null;
    }
}
