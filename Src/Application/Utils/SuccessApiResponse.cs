using System.Text.Json.Serialization;
using Application.DTOs.Misc;
using Domain.Exceptions;

namespace Application.Utils;
public class SuccessApiResponse<T>
{
    public bool Success { get; set; } = true;
    public int StatusCode { get; set; }
    public string Message { get; set; }
    public T Data { get; set; }
    public string TraceId {get; set;}

    public SuccessApiResponse(SuccessApiResponseDto<T> dto)
    {
        if (StatusCodeUtils.IsFailure(dto.StatusCode))
        {
            throw new SuccessStatusNotAlignedWithStatusCodeException([]);
        }
        Success = true;
        StatusCode = dto.StatusCode;
        Message = dto.Message;
        Data = dto.Data;
        TraceId = dto.TraceId;
    }
}