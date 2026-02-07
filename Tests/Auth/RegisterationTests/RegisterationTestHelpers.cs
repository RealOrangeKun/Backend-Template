using System.Net.Http.Json;
using System.Text.Json;
using Application.DTOs.InternalAuth;

namespace Tests.Auth;

public static class RegisterationTestHelpers
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static async Task<(HttpResponseMessage Response, TResponse? Content, string Json)>
        PostRegisterAsync<TResponse>(HttpClient client, RegisterRequestDto request)
    {
        var response = await client.PostAsJsonAsync("/api/internal-auth/register", request);
        var json = await response.Content.ReadAsStringAsync();
        var content = JsonSerializer.Deserialize<TResponse>(json, JsonOptions);
        return (response, content, json);
    }
}
