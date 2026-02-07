using System.Text.Json.Serialization;

namespace Tests.MailHog;

public class MailhogMessagesResponse
{
    [JsonPropertyName("total")]
    public int Total { get; set; }

    [JsonPropertyName("count")]
    public int Count { get; set; }

    [JsonPropertyName("start")]
    public int Start { get; set; }

    [JsonPropertyName("items")]
    public List<MailhogMessage> Items { get; set; } = [];
}
