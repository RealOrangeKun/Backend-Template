using Application.DTOs.Misc;
using Application.Utils;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;
[ApiController]
[ProducesResponseType(typeof(FailApiResponse), StatusCodes.Status400BadRequest)]
[ProducesResponseType(typeof(FailApiResponse), StatusCodes.Status500InternalServerError)]
public abstract class BaseApiController : ControllerBase
{
    protected IActionResult SuccessResponse<T>(T data, int statusCode, string message)
    {
        var dto = new SuccessApiResponseDto<T>
        {
            Data = data,
            StatusCode = statusCode,
            Message = message,
            TraceId = HttpContext.TraceIdentifier
        };
        var response = statusCode switch
        {
            StatusCodes.Status201Created => SuccessApiResponse<T>.Created(dto),
            StatusCodes.Status202Accepted => SuccessApiResponse<T>.Accepted(dto),
            StatusCodes.Status204NoContent => SuccessApiResponse<T>.NoContent(dto),
            _ => SuccessApiResponse<T>.Ok(dto)
        };
        return StatusCode(statusCode, response);
    }
}