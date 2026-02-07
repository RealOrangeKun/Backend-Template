using System.Text.Json.Serialization;

namespace Tests.MailHog;
public class MailhogMessage
{
    [JsonPropertyName("ID")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("From")]
    public MailhogPath From { get; set; } = new();

    [JsonPropertyName("To")]
    public List<MailhogPath> To { get; set; } = [];

    [JsonPropertyName("Content")]
    public MailhogContent Content { get; set; } = new();

    [JsonPropertyName("Created")]
    public DateTime Created { get; set; }

    [JsonPropertyName("MIME")]
    public MailhogMime? Mime { get; set; }

    [JsonPropertyName("Raw")]
    public MailhogRaw Raw { get; set; } = new();
}
