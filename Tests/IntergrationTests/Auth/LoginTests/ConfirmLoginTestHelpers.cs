using System.Net.Http.Json;
using System.Text.Json;
using Application.DTOs.Auth;

namespace Tests.Auth;

public static class ConfirmLoginTestHelpers
{
    public static async Task<(HttpResponseMessage Response, T? Content, string? RawContent)> PostConfirmLoginAsync<T>(HttpClient client, ConfirmLoginRequestDto request)
    {
        var response = await client.PostAsJsonAsync("/api/v1/internal-auth/confirm-login", request);
        var rawContent = await response.Content.ReadAsStringAsync();

        T? content = default;
        if (!string.IsNullOrWhiteSpace(rawContent))
        {
            try
            {
                content = JsonSerializer.Deserialize<T>(rawContent, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            }
            catch
            {
                // Ignore deserialization errors for this helper
            }
        }

        return (response, content, rawContent);
    }
}
