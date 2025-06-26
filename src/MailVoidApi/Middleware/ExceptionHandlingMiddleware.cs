using System.Net;
using System.Text.Json;

namespace MailVoidApi.Middleware;

public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var request = context.Request;
        var requestBody = await GetRequestBodyAsync(request);
        
        _logger.LogError(exception, 
            "Unhandled exception occurred. Method: {Method}, Path: {Path}, Query: {Query}, Headers: {@Headers}, Body: {Body}, User: {User}, IP: {IP}",
            request.Method,
            request.Path,
            request.QueryString,
            request.Headers.ToDictionary(h => h.Key, h => h.Value.ToString()),
            requestBody,
            context.User?.Identity?.Name ?? "Anonymous",
            GetClientIpAddress(context));

        var response = context.Response;
        response.ContentType = "application/json";

        var (statusCode, message) = GetErrorResponse(exception);
        response.StatusCode = (int)statusCode;

        var errorResponse = new
        {
            error = new
            {
                message = message,
                type = exception.GetType().Name,
                timestamp = DateTime.UtcNow,
                path = request.Path.Value,
                method = request.Method
            }
        };

        var jsonResponse = JsonSerializer.Serialize(errorResponse, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        await response.WriteAsync(jsonResponse);
    }

    private static async Task<string> GetRequestBodyAsync(HttpRequest request)
    {
        try
        {
            if (request.ContentLength == 0 || request.ContentLength == null)
                return string.Empty;

            request.EnableBuffering();
            request.Body.Position = 0;

            using var reader = new StreamReader(request.Body, leaveOpen: true);
            var body = await reader.ReadToEndAsync();
            request.Body.Position = 0;

            return body.Length > 1000 ? $"{body[..1000]}... (truncated)" : body;
        }
        catch
        {
            return "[Unable to read request body]";
        }
    }

    private static string GetClientIpAddress(HttpContext context)
    {
        var ipAddress = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrEmpty(ipAddress))
        {
            return ipAddress.Split(',')[0].Trim();
        }

        ipAddress = context.Request.Headers["X-Real-IP"].FirstOrDefault();
        if (!string.IsNullOrEmpty(ipAddress))
        {
            return ipAddress;
        }

        return context.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
    }

    private static (HttpStatusCode statusCode, string message) GetErrorResponse(Exception exception)
    {
        return exception switch
        {
            ArgumentNullException => (HttpStatusCode.BadRequest, "Missing required parameters"),
            ArgumentException => (HttpStatusCode.BadRequest, "Invalid request parameters"),
            UnauthorizedAccessException => (HttpStatusCode.Unauthorized, "Unauthorized access"),
            NotImplementedException => (HttpStatusCode.NotImplemented, "Feature not implemented"),
            TimeoutException => (HttpStatusCode.RequestTimeout, "Request timeout"),
            _ => (HttpStatusCode.InternalServerError, "An internal server error occurred")
        };
    }
}