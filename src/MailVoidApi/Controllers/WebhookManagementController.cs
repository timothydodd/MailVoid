using MailVoidApi.Common;
using MailVoidApi.Data;
using MailVoidApi.Models;
using MailVoidApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RoboDodd.OrmLite;

namespace MailVoidApi.Controllers;

[Authorize]
[ApiController]
[Route("api/webhooks")]
public class WebhookManagementController : ControllerBase
{
    private readonly ILogger<WebhookManagementController> _logger;
    private readonly IDatabaseService _db;
    private readonly IUserService _userService;

    public WebhookManagementController(
        ILogger<WebhookManagementController> logger,
        IDatabaseService db,
        IUserService userService)
    {
        _logger = logger;
        _db = db;
        _userService = userService;
    }

    [HttpGet("buckets")]
    public async Task<IActionResult> GetBuckets()
    {
        using var db = await _db.GetConnectionAsync();
        var buckets = await db.SelectAsync<WebhookBucket>();

        var result = buckets.Select(b => new
        {
            b.Id,
            b.Name,
            b.Description,
            b.IsPublic,
            b.CreatedAt,
            b.LastActivity,
            b.RetentionDays
        }).OrderByDescending(b => b.LastActivity ?? b.CreatedAt).ToList();

        return Ok(result);
    }

    [HttpGet("buckets/{name}")]
    public async Task<IActionResult> GetBucket(string name)
    {
        using var db = await _db.GetConnectionAsync();
        var bucket = await db.SingleAsync<WebhookBucket>(b => b.Name == name);

        if (bucket == null)
        {
            return NotFound();
        }

        return Ok(new
        {
            bucket.Id,
            bucket.Name,
            bucket.Description,
            bucket.IsPublic,
            bucket.CreatedAt,
            bucket.LastActivity,
            bucket.RetentionDays
        });
    }

    [HttpGet("buckets/{name}/webhooks")]
    public async Task<ActionResult<PagedResults<Webhook>>> GetWebhooks(
        string name,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        using var db = await _db.GetConnectionAsync();

        var bucket = await db.SingleAsync<WebhookBucket>(b => b.Name == name);
        if (bucket == null)
        {
            return NotFound("Bucket not found");
        }

        var offset = (page - 1) * pageSize;

        // Get total count
        var totalCount = await db.CountAsync<Webhook>(w => w.BucketName == name);

        // Get webhooks using SqlExpression
        var query = db.From<Webhook>()
            .Where(w => w.BucketName == name)
            .OrderByDescending(w => w.CreatedOn)
            .Limit(pageSize, offset);

        var webhooks = await db.SelectAsync(query);

        return new PagedResults<Webhook>
        {
            Items = webhooks,
            TotalCount = totalCount
        };
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetWebhook(long id)
    {
        using var db = await _db.GetConnectionAsync();
        var webhook = await db.SingleByIdAsync<Webhook>(id);

        if (webhook == null)
        {
            return NotFound();
        }

        return Ok(new
        {
            webhook.Id,
            webhook.BucketName,
            webhook.HttpMethod,
            webhook.Path,
            webhook.QueryString,
            webhook.Headers,
            webhook.Body,
            webhook.ContentType,
            webhook.SourceIp,
            webhook.CreatedOn
        });
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteWebhook(long id)
    {
        using var db = await _db.GetConnectionAsync();
        var webhook = await db.SingleByIdAsync<Webhook>(id);

        if (webhook == null)
        {
            return NotFound();
        }

        await db.DeleteAsync(webhook);
        return NoContent();
    }

    [HttpDelete("buckets/{name}")]
    public async Task<IActionResult> DeleteBucket(string name)
    {
        using var db = await _db.GetConnectionAsync();

        var bucket = await db.SingleAsync<WebhookBucket>(b => b.Name == name);
        if (bucket == null)
        {
            return NotFound();
        }

        // Delete all webhooks in the bucket first
        await db.DeleteAsync<Webhook>(w => w.BucketName == name);

        // Delete the bucket
        await db.DeleteAsync(bucket);

        return NoContent();
    }
}

