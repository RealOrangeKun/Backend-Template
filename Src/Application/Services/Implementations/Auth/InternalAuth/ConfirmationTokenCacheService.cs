using Microsoft.Extensions.Caching.Distributed;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Application.Services.Implementations;

public class ConfirmationTokenCacheService(IDistributedCache cache)
{
    private readonly IDistributedCache _cache = cache;

    public static string GenerateRandomToken()
    {
        return new Random().Next(100000, 999999).ToString();
    }

    public async Task SetTokenAsync(string token, Guid userId, CancellationToken cancellationToken)
    {
        await _cache.SetStringAsync(token, userId.ToString(), new DistributedCacheEntryOptions
        {            
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10)
        }, cancellationToken);
    }

    public async Task<Guid> GetUserIdByTokenAsync(string token, CancellationToken cancellationToken)
    {
        var tokenValue = await _cache.GetStringAsync(token, cancellationToken);
        return tokenValue != null ? Guid.Parse(tokenValue) : Guid.Empty;
    }

    public async Task DeleteTokenAsync(string token, CancellationToken cancellationToken)
    {
        await _cache.RemoveAsync(token, cancellationToken);
    }
}
