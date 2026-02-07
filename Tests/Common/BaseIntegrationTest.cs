using Tests.Common;
using Xunit;

namespace Tests.Common;

[Collection("Integration Tests")]
public abstract class BaseIntegrationTest : IAsyncLifetime
{
    protected readonly CustomWebApplicationFactory Factory;
    protected readonly HttpClient Client;

    protected BaseIntegrationTest(CustomWebApplicationFactory factory)
    {
        Factory = factory;
        Client = factory.CreateClient();
    }

    public virtual async Task InitializeAsync()
    {
        await Task.WhenAll(
            Factory.ResetDatabaseAsync(),
            Factory.ResetRedisAsync()
        );

        if (Factory.MailhogClient != null)
        {
            await Factory.MailhogClient.DeleteAllMessagesAsync();
        }
    }

    public virtual Task DisposeAsync() => Task.CompletedTask;
}
