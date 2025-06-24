using System;
using System.Collections.Generic;

namespace MailVoidSmtpServer.Models;

public class QueuedEmail
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public EmailWebhookData EmailData { get; set; } = new();
    public DateTime QueuedAt { get; set; } = DateTime.UtcNow;
    public int ProcessingAttempts { get; set; } = 0;
    public DateTime? LastAttemptAt { get; set; }
    public string? LastError { get; set; }
    public bool IsProcessing { get; set; }
    public int Priority { get; set; } = 0; // Higher number = higher priority
}

public class OutboundQueueItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public EmailWebhookData EmailData { get; set; } = new();
    public DateTime QueuedAt { get; set; } = DateTime.UtcNow;
    public int RetryAttempts { get; set; } = 0;
    public DateTime? NextRetryAt { get; set; }
    public string? LastError { get; set; }
    public bool IsProcessing { get; set; }
    public int Priority { get; set; } = 0;
}

public class EmailWebhookData
{
    public string From { get; set; } = "";
    public List<string> To { get; set; } = new();
    public string Subject { get; set; } = "";
    public string? Html { get; set; }
    public string? Text { get; set; }
    public Dictionary<string, string> Headers { get; set; } = new();
    public List<AttachmentData> Attachments { get; set; } = new();
    public string? MessageId { get; set; }
    public DateTime Date { get; set; }
    public string RawEmail { get; set; } = "";
}

public class AttachmentData
{
    public string Filename { get; set; } = "";
    public string ContentType { get; set; } = "";
    public string Content { get; set; } = ""; // Base64 encoded
}