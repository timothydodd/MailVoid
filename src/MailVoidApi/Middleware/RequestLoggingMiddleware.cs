namespace MailVoidApi.Middleware;

public class RequestLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestLoggingMiddleware> _logger;

    public RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var request = context.Request;
        
        // Skip logging for static files and health checks to reduce noise
        if (ShouldSkipLogging(request.Path))
        {
            await _next(context);
            return;
        }

        var startTime = DateTime.UtcNow;
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        _logger.LogInformation("HTTP {Method} {Path}{Query} started - User: {User}, IP: {IP}, UserAgent: {UserAgent}",
            request.Method,
            request.Path,
            request.QueryString,
            context.User?.Identity?.Name ?? "Anonymous",
            GetClientIpAddress(context),
            request.Headers.UserAgent.ToString());

        try
        {
            await _next(context);
        }
        finally
        {
            stopwatch.Stop();
            var response = context.Response;
            
            var logLevel = response.StatusCode >= 400 ? LogLevel.Warning : LogLevel.Information;
            
            _logger.Log(logLevel, "HTTP {Method} {Path}{Query} completed - Status: {StatusCode}, Duration: {Duration}ms, User: {User}",
                request.Method,
                request.Path,
                request.QueryString,
                response.StatusCode,
                stopwatch.ElapsedMilliseconds,
                context.User?.Identity?.Name ?? "Anonymous");
        }
    }

    private static bool ShouldSkipLogging(PathString path)
    {
        var pathValue = path.Value?.ToLowerInvariant();
        return pathValue != null && (
            pathValue.StartsWith("/css/") ||
            pathValue.StartsWith("/js/") ||
            pathValue.StartsWith("/images/") ||
            pathValue.StartsWith("/fonts/") ||
            pathValue.StartsWith("/favicon.ico") ||
            pathValue.StartsWith("/_framework/") ||
            pathValue.Contains(".min.") ||
            pathValue.EndsWith(".js") ||
            pathValue.EndsWith(".css") ||
            pathValue.EndsWith(".ico") ||
            pathValue.EndsWith(".png") ||
            pathValue.EndsWith(".jpg") ||
            pathValue.EndsWith(".gif") ||
            pathValue.EndsWith(".svg")
        );
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
}