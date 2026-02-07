using Application.DTOs.Misc;
using Microsoft.AspNetCore.Http;

namespace Application.Utils;
public record SuccessApiResponse<T>
{
    public bool Success { get; init; } = true;
    public int StatusCode { get; init; }
    public string Message { get; init; } = string.Empty;
    public T Data { get; init; } = default!;
    public string TraceId { get; init; } = string.Empty;

    public SuccessApiResponse()
    {
    }

    private SuccessApiResponse(int statusCode, SuccessApiResponseDto<T> successResponse)
    {
        StatusCode = statusCode;
        Message = successResponse.Message;
        Data = successResponse.Data;
        TraceId = successResponse.TraceId;
    }

    public static SuccessApiResponse<T> Ok(SuccessApiResponseDto<T> successApiResponse)
    {
        return new SuccessApiResponse<T>(StatusCodes.Status200OK, successApiResponse);
    }

    public static SuccessApiResponse<T> Created(SuccessApiResponseDto<T> successApiResponse)
    {
        return new SuccessApiResponse<T>((int)StatusCodes.Status201Created, successApiResponse);
    }

    public static SuccessApiResponse<T> Accepted(SuccessApiResponseDto<T> successApiResponse)
    {
        return new SuccessApiResponse<T>((int)StatusCodes.Status202Accepted, successApiResponse);
    }

    public static SuccessApiResponse<T> NoContent(SuccessApiResponseDto<T> successApiResponse)
    {
        return new SuccessApiResponse<T>((int)StatusCodes.Status204NoContent, successApiResponse);
    }
}