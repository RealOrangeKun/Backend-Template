using System.Net;
using System.Text.Json;
using Application.Constants;
using Application.DTOs.Misc;
using Application.Utils;
using Domain.Exceptions;

namespace MyBackendTemplate.API.Middlewares;
public class GlobalExceptionHandlerMiddleware(RequestDelegate next, ILogger<GlobalExceptionHandlerMiddleware> logger)
{
    private readonly RequestDelegate _next = next;
    private readonly ILogger<GlobalExceptionHandlerMiddleware> _logger = logger;

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "An exception was thrown while processing the request.");
            await HandleExceptionAsync(context, ex);
        }
    }

    private static Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        context.Response.ContentType = "application/json";
        var response = new FailApiResponseDto();
        if (exception is DomainException domainException)
        {
            response = new FailApiResponseDto
            {
                Message = domainException.Message,
                Errors = [],
                ErrorCode = ErrorCodes.DomainErrorCode,
                TraceId = context.TraceIdentifier

            };
            context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
        }
        else
        {
            response = new FailApiResponseDto
            {
                Message = "An unexpected error occurred. Please try again later.",
                Errors = [],
                ErrorCode = ErrorCodes.InternalServerErrorCode,
                TraceId = context.TraceIdentifier
            };
            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
        }

        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var json = JsonSerializer.Serialize(FailApiResponse.InternalServerError(response), options);
        return context.Response.WriteAsync(json);
    }
}
