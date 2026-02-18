using Microsoft.AspNetCore.Http;
using System.Net;

namespace Tests.Common;

/// <summary>
/// Middleware that injects a fake RemoteIpAddress and DeviceId into the HttpContext for testing purposes.
/// This allows the GetClientIpAddress() and GetDeviceIdCookie() extensions to work properly in integration tests.
/// </summary>
public class FakeRemoteIpAddressMiddleware
{
    private readonly RequestDelegate _next;
    public const string DefaultTestIpAddress = "192.168.1.100";

    public FakeRemoteIpAddressMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Check if X-Test-Remote-IP header is present (set by test client)
        if (context.Request.Headers.TryGetValue("X-Test-Remote-IP", out var customIp))
        {
            if (IPAddress.TryParse(customIp.ToString(), out var testIp))
            {
                context.Connection.RemoteIpAddress = testIp;
            }
        }
        // If no header, use default test IP
        else if (IPAddress.TryParse(DefaultTestIpAddress, out var defaultIp))
        {
            context.Connection.RemoteIpAddress = defaultIp;
        }

        // Handle device ID injection for tests
        if (context.Request.Headers.TryGetValue("X-Test-Device-ID", out var deviceIdHeader))
        {
            // Inject as cookie for the controller to read
            context.Request.Headers.Append("Cookie", $"deviceId={deviceIdHeader}");
        }

        await _next(context);
    }
}
