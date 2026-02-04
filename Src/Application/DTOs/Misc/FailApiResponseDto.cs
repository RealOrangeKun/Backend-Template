namespace Application.DTOs.Misc;

public class FailApiResponseDto
{
    public string Message { get; set; } = default!;
    public int StatusCode { get; set; }
    public Dictionary<string, string[]> Errors { get; set; } = [];
    public string ErrorCode { get; set; } = default!;
    public string TraceId { get; set; } = default!;
}
