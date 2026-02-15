using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Text.Json;
using Application.DTOs.Auth;

namespace Tests.Auth;

public static class GuestPromoteTestHelpers
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static async Task<(HttpResponseMessage Response, TResponse? Content, string Json)>
        PostGuestPromoteAsync<TResponse>(HttpClient client, RegisterRequestDto request, string accessToken)
    {
        var requestMessage = new HttpRequestMessage(HttpMethod.Post, "/api/v1/internal-auth/guest-promote")
        {
            Content = JsonContent.Create(request)
        };

        // Add authorization header with the guest user's access token
        requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        
        // Add idempotency key header (required for this endpoint)
        requestMessage.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var response = await client.SendAsync(requestMessage);
        var json = await response.Content.ReadAsStringAsync();
        
        // Only try to deserialize if the response is likely JSON (not 401 Unauthorized which returns HTML)
        TResponse? content = default;
        if (response.StatusCode != System.Net.HttpStatusCode.Unauthorized && !string.IsNullOrWhiteSpace(json))
        {
            try
            {
                content = JsonSerializer.Deserialize<TResponse>(json, JsonOptions);
            }
            catch (JsonException)
            {
                // If deserialization fails, leave content as null
            }
        }

        return (response, content, json);
    }
}
