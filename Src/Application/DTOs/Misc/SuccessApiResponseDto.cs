namespace Application.DTOs.Misc;
public record SuccessApiResponseDto<T>
{
    public string Message { get; init; } = default!;
    public int StatusCode { get; init; }
    public T Data { get; init; } = default!;
    public string TraceId { get; init; } = default!;
}