using Application.DTOs.Misc;
using Microsoft.AspNetCore.Http;

namespace Application.Utils;
public record FailApiResponse
{
    public bool Success { get; init; } = false;
    public int StatusCode { get; init; }
    public string Message { get; init; } = string.Empty;
    public Dictionary<string, string[]> Errors { get; init; } = [];
    public string ErrorCode { get; init; } = string.Empty;
    public string TraceId { get; init; } = string.Empty;

    public FailApiResponse()
    {
    }

    private FailApiResponse(int statusCode, FailApiResponseDto failResponse)
    {
        StatusCode = statusCode;
        Message = failResponse.Message;
        Errors = failResponse.Errors;
        ErrorCode = failResponse.ErrorCode;
        TraceId = failResponse.TraceId;
    }

    public static FailApiResponse BadRequest(FailApiResponseDto failApiResponse)
    {
        return new FailApiResponse(StatusCodes.Status400BadRequest, failApiResponse);
    }

    public static FailApiResponse NotFound(FailApiResponseDto failApiResponse)
    {
        return new FailApiResponse(StatusCodes.Status404NotFound, failApiResponse);
    }

    public static FailApiResponse Conflict(FailApiResponseDto failApiResponse)
    {
        return new FailApiResponse(StatusCodes.Status409Conflict, failApiResponse);
    }

    public static FailApiResponse InternalServerError(FailApiResponseDto failApiResponse)
    {
        return new FailApiResponse(StatusCodes.Status500InternalServerError, failApiResponse);
    }
}