namespace Application.DTOs.Misc;
public record FailApiResponseDto
{
    public string Message { get; init; } = default!;
    public Dictionary<string, string[]> Errors { get; init; } = [];
    public string ErrorCode { get; init; } = default!;
    public string TraceId { get; init; } = default!;
}