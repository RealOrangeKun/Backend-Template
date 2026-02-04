using System.Text.Json.Serialization;
using Application.DTOs.Misc;
using Domain.Exceptions;

namespace Application.Utils;
public class FailApiResponse<T>
{
    public bool Success { get; set; } = false;
    public int StatusCode { get; set; }
    public string Message { get; set; }
    public Dictionary<string, string[]> Errors { get; set; } = [];
    public string ErrorCode { get; set; }
    public string TraceId {get; set;}

    public FailApiResponse(FailApiResponseDto dto)
    {
        if (StatusCodeUtils.IsSuccess(dto.StatusCode))
        {
            throw new SuccessStatusNotAlignedWithStatusCodeException(dto.Errors);
        }
        Success = true;
        StatusCode = dto.StatusCode;
        Message = dto.Message;
        Errors = dto.Errors;
        ErrorCode = dto.ErrorCode;
        TraceId = dto.TraceId;
    }
}