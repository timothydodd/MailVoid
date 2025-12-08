using System.Text.Json;
using MailVoidApi.Data;
using MailVoidApi.Hubs;
using MailVoidApi.Models;
using MailVoidApi.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using RoboDodd.OrmLite;

namespace MailVoidApi.Controllers;

[ApiController]
[Route("api/hook")]
public class HooksController : ControllerBase
{
    private readonly ILogger<HooksController> _logger;
    private readonly IDatabaseService _db;
    private readonly IWebhookBucketService _bucketService;
    private readonly IHubContext<MailNotificationHub> _hubContext;

    public HooksController(
        ILogger<HooksController> logger,
        IDatabaseService db,
        IWebhookBucketService bucketService,
        IHubContext<MailNotificationHub> hubContext)
    {
        _logger = logger;
        _db = db;
        _bucketService = bucketService;
        _hubContext = hubContext;
    }

    [HttpPost("{bucket}")]
    [HttpPut("{bucket}")]
    [HttpPatch("{bucket}")]
    public Task<IActionResult> CaptureWebhook(string bucket)
    {
        return CaptureWebhookWithPath(bucket, "");
    }

    [HttpPost("{bucket}/{**path}")]
    [HttpPut("{bucket}/{**path}")]
    [HttpPatch("{bucket}/{**path}")]
    public async Task<IActionResult> CaptureWebhookWithPath(string bucket, string path)
    {
        _logger.LogInformation("Received webhook for bucket {Bucket}, path {Path}, method {Method}",
            bucket, path, Request.Method);

        try
        {
            // Ensure bucket exists
            var webhookBucket = await _bucketService.GetOrCreateBucket(bucket);

            // Read body
            string body;
            using (var reader = new StreamReader(Request.Body))
            {
                body = await reader.ReadToEndAsync();
            }

            // Capture headers as JSON
            var headers = Request.Headers
                .Where(h => !h.Key.StartsWith("X-Forwarded", StringComparison.OrdinalIgnoreCase))
                .ToDictionary(h => h.Key, h => h.Value.ToString());
            var headersJson = JsonSerializer.Serialize(headers);

            // Get source IP
            var sourceIp = Request.HttpContext.Connection.RemoteIpAddress?.ToString();

            using var db = await _db.GetConnectionAsync();

            var webhook = new Webhook
            {
                BucketName = bucket,
                HttpMethod = Request.Method,
                Path = string.IsNullOrEmpty(path) ? "/" : $"/{path}",
                QueryString = Request.QueryString.HasValue ? Request.QueryString.Value : null,
                Headers = headersJson,
                Body = body,
                ContentType = Request.ContentType,
                SourceIp = sourceIp,
                CreatedOn = DateTime.UtcNow
            };

            await db.InsertAsync(webhook);

            // Update bucket's last activity
            webhookBucket.LastActivity = DateTime.UtcNow;
            await db.UpdateAsync(webhookBucket);

            _logger.LogInformation("Successfully captured webhook for bucket {Bucket}", bucket);

            // Send SignalR notification
            await _hubContext.Clients.All.SendAsync("NewWebhook", new
            {
                id = webhook.Id,
                bucketName = webhook.BucketName,
                httpMethod = webhook.HttpMethod,
                path = webhook.Path,
                contentType = webhook.ContentType,
                createdOn = webhook.CreatedOn
            });

            return Ok(new { message = "Webhook captured", id = webhook.Id });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to capture webhook for bucket {Bucket}", bucket);
            return StatusCode(500, "Failed to capture webhook");
        }
    }
}
