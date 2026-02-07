using System.Text.Json.Serialization;

namespace Tests.MailHog;
public class MailhogContent
{
    [JsonPropertyName("Headers")]
    public Dictionary<string, List<string>> Headers { get; set; } = [];

    [JsonPropertyName("Body")]
    public string Body { get; set; } = string.Empty;

    [JsonPropertyName("Size")]
    public int Size { get; set; }

    [JsonPropertyName("MIME")]
    public object? Mime { get; set; }
}
