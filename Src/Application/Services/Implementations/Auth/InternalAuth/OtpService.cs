using System.Security.Cryptography;
using System.Text.Json;
using Application.Services.Interfaces.Auth.InternalAuth;
using Microsoft.Extensions.Caching.Distributed;
using Application.DTOs.Auth.InternalAuth;

namespace Application.Services.Implementations.Auth.InternalAuth;

public class OtpService<T>(
    IDistributedCache cache,
    IOtpStrategy<T> strategy
    ) : IOtpService<T> where T : class
{
    private readonly IDistributedCache _cache = cache;
    private readonly IOtpStrategy<T> _strategy = strategy;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task CacheAsync(T payload, string otp, CancellationToken cancellationToken)
    {
        var key = BuildKey(_strategy.KeyPrefix, otp);
        var value = JsonSerializer.Serialize(payload, JsonOptions);

        await _cache.SetStringAsync(key, value, new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = _strategy.Expiration
        }, cancellationToken);
    }

    public async Task<T> GetDataAsync(string otp, CancellationToken cancellationToken)
    {
        var key = BuildKey(_strategy.KeyPrefix, otp);
        var cachedValue = await _cache.GetStringAsync(key, cancellationToken);
        if (string.IsNullOrWhiteSpace(cachedValue))
        {
            return default!;
        }

        try
        {
            if (cachedValue.TrimStart().StartsWith('{'))
            {
                return JsonSerializer.Deserialize<T>(cachedValue, JsonOptions)!;
            }
        }
        catch (JsonException) { }

        // Legacy format (plain text in tests)
        if (typeof(T) == typeof(RegistrationOtpPayload) && Guid.TryParse(cachedValue, out var regUserId))
        {
            return (new RegistrationOtpPayload(regUserId) as T)!;
        }
        if (typeof(T) == typeof(PasswordResetOtpPayload) && Guid.TryParse(cachedValue, out var pwdUserId))
        {
            return (new PasswordResetOtpPayload(pwdUserId) as T)!;
        }
        if (typeof(T) == typeof(NewDeviceOtpPayload))
        {
            var parts = cachedValue.Split(':');
            if (parts.Length == 2 && Guid.TryParse(parts[0], out var dUserId) && Guid.TryParse(parts[1], out var dDeviceId))
            {
                return (new NewDeviceOtpPayload(dUserId, dDeviceId) as T)!;
            }
        }

        return default!;
    }

    public async Task<bool> IsOtpValidAsync(T payload, string otp, CancellationToken cancellationToken)
    {
        var key = BuildKey(_strategy.KeyPrefix, otp);
        var cachedValue = await _cache.GetStringAsync(key, cancellationToken);
        if (string.IsNullOrWhiteSpace(cachedValue))
        {
            return false;
        }
        var payloadJson = JsonSerializer.Serialize(payload, JsonOptions);
        return string.Equals(payloadJson, cachedValue, StringComparison.Ordinal);
    }

    private static string BuildKey(string prefix, string otp)
    {
        return $"{prefix}{otp}";
    }
}