using System.Text.Json.Serialization;

namespace Tests.MailHog;

public class MailhogMime
{
    [JsonPropertyName("Parts")]
    public List<MailhogMimePart>? Parts { get; set; }
}
