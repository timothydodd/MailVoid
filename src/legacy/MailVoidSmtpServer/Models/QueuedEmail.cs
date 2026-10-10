using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MailVoidSmtpServer.Models;

public class QueuedEmail
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public EmailWebhookData EmailData { get; set; } = new();
    public DateTime QueuedAt { get; set; } = DateTime.UtcNow;
    public int Attempts { get; set; }
    public DateTime? LastAttemptAt { get; set; }
    public string? LastError { get; set; }

    // Absolute path of the on-disk spool file backing this item. Not serialized into the file itself.
    [JsonIgnore]
    public string? SpoolPath { get; set; }

    // In-memory retry scheduling; recovered items simply retry immediately after a restart.
    [JsonIgnore]
    public DateTime? NextRetryAt { get; set; }
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
    public bool ParseFailed { get; set; }
}

public class AttachmentData
{
    public string Filename { get; set; } = "";
    public string ContentType { get; set; } = "";
    public string Content { get; set; } = ""; // Base64 encoded
}
