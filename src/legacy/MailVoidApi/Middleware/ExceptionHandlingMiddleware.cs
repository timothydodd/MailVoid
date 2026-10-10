using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

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

    private static readonly HashSet<string> SensitiveHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Authorization", "Cookie", "Set-Cookie", "X-Api-Key"
    };

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var request = context.Request;

        // Do not log credentials: redact sensitive headers and never log the request body.
        var safeHeaders = request.Headers.ToDictionary(
            h => h.Key,
            h => SensitiveHeaders.Contains(h.Key) ? "***" : h.Value.ToString());

        _logger.LogError(exception,
            "Unhandled exception occurred. Method: {Method}, Path: {Path}, Query: {Query}, Headers: {@Headers}, User: {User}, IP: {IP}",
            request.Method,
            request.Path,
            RedactQueryString(request.QueryString),
            safeHeaders,
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

    private static string RedactQueryString(QueryString queryString)
    {
        if (!queryString.HasValue)
            return string.Empty;

        return Regex.Replace(
            queryString.Value!,
            @"([?&](?:api_key|access_token)=)[^&]*",
            "$1***",
            RegexOptions.IgnoreCase);
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