namespace Application.DTOs.Misc;

public class SuccessApiResponseDto<T>
{
    public string Message { get; set; } = default!;
    public int StatusCode { get; set; }
    public T Data { get; set; } = default!;
    public string TraceId { get; set; } = default!;
}
