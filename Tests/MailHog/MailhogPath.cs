using System.Text.Json.Serialization;

namespace Tests.MailHog;

public class MailhogPath
{
    [JsonPropertyName("Relays")]
    public object? Relays { get; set; }

    [JsonPropertyName("Mailbox")]
    public string Mailbox { get; set; } = string.Empty;

    [JsonPropertyName("Domain")]
    public string Domain { get; set; } = string.Empty;

    [JsonPropertyName("Params")]
    public string Params { get; set; } = string.Empty;

    public string Email => $"{Mailbox}@{Domain}";
}
