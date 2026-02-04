using System.Net;
using System.Text.Json;
using Application.DTOs.Misc;
using Application.Utils;
using Domain.Exceptions;

namespace MyBackendTemplate.API.Middlewares;

public class GlobalExceptionHandlerMiddleware(RequestDelegate next, ILogger<GlobalExceptionHandlerMiddleware> logger)
{
    private readonly RequestDelegate _next = next;
    private readonly ILogger<GlobalExceptionHandlerMiddleware> _logger = logger;

    public async Task InvokeAsync(HttpContext context, CancellationToken cancellationToken)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            await HandleExceptionAsync(context, ex, cancellationToken);
        }
    }

    private Task HandleExceptionAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        context.Response.ContentType = "application/json";
        var response = new FailApiResponseDto();
        
        if (exception is CustomAppException customAppException)
        {
            _logger.LogInformation(customAppException, "A CustomAppException was thrown while processing the request.");
            response.Message = customAppException.Message;
            response.StatusCode = customAppException.StatusCode;
            response.Errors = customAppException.Errors;
            response.ErrorCode = customAppException.ErrorCode;
            context.Response.StatusCode = customAppException.StatusCode;
        }
        else
        {
            _logger.LogError(exception, "An Unhandled Exception occurred while processing the request.");
            response.Message = "An unexpected error occurred.";
            response.StatusCode = (int)HttpStatusCode.InternalServerError;
            response.Errors = [];
            response.ErrorCode = "INTERNAL_SERVER_ERROR";
            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
        }

        response.TraceId = context.TraceIdentifier;
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var json = JsonSerializer.Serialize(response, options);

        return context.Response.WriteAsync(json, cancellationToken);
    }
}
