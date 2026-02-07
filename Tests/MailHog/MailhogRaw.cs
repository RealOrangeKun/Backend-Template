using System.Text.Json.Serialization;

namespace Tests.MailHog;

public class MailhogRaw
{
    [JsonPropertyName("From")]
    public string From { get; set; } = string.Empty;

    [JsonPropertyName("To")]
    public List<string> To { get; set; } = [];

    [JsonPropertyName("Data")]
    public string Data { get; set; } = string.Empty;

    [JsonPropertyName("Helo")]
    public string Helo { get; set; } = string.Empty;
}
