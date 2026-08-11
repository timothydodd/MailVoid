using System.Text;
using MailVoidSmtpServer.Services;
using MimeKit;

namespace MailVoidSmtpServer.Tests;

public class MimeExtractorTests
{
    private static MimeMessage Load(string raw)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(raw));
        return MimeMessage.Load(stream);
    }

    private static readonly string MultipartWithAttachment =
        "From: sender@example.com\r\n" +
        "To: recipient@mailvoid.com\r\n" +
        "Subject: Hello\r\n" +
        "MIME-Version: 1.0\r\n" +
        "Content-Type: multipart/mixed; boundary=\"BOUND\"\r\n" +
        "\r\n" +
        "--BOUND\r\n" +
        "Content-Type: multipart/alternative; boundary=\"ALT\"\r\n" +
        "\r\n" +
        "--ALT\r\n" +
        "Content-Type: text/plain; charset=utf-8\r\n" +
        "\r\n" +
        "Hello in plain text\r\n" +
        "--ALT\r\n" +
        "Content-Type: text/html; charset=utf-8\r\n" +
        "\r\n" +
        "<p>Hello in HTML</p>\r\n" +
        "--ALT--\r\n" +
        "--BOUND\r\n" +
        "Content-Type: text/plain; name=\"note.txt\"\r\n" +
        "Content-Disposition: attachment; filename=\"note.txt\"\r\n" +
        "Content-Transfer-Encoding: base64\r\n" +
        "\r\n" +
        Convert.ToBase64String(Encoding.UTF8.GetBytes("file body")) + "\r\n" +
        "--BOUND--\r\n";

    [Fact]
    public void GetDecodedTextBody_ReturnsPlainText()
    {
        var message = Load(MultipartWithAttachment);
        Assert.Equal("Hello in plain text", MimeExtractor.GetDecodedTextBody(message));
    }

    [Fact]
    public void GetDecodedHtmlBody_ReturnsHtml()
    {
        var message = Load(MultipartWithAttachment);
        Assert.Contains("Hello in HTML", MimeExtractor.GetDecodedHtmlBody(message));
    }

    [Fact]
    public void ExtractAttachments_ReturnsDecodableAttachment()
    {
        var message = Load(MultipartWithAttachment);
        var attachments = MimeExtractor.ExtractAttachments(message);

        var att = Assert.Single(attachments);
        Assert.Equal("note.txt", att.Filename);
        Assert.Equal("text/plain", att.ContentType);
        Assert.Equal("file body", Encoding.UTF8.GetString(Convert.FromBase64String(att.Content)));
    }

    [Fact]
    public void PlainOnlyMessage_HasNoHtmlBody()
    {
        var raw =
            "From: a@example.com\r\n" +
            "To: b@mailvoid.com\r\n" +
            "Subject: Plain\r\n" +
            "Content-Type: text/plain; charset=utf-8\r\n" +
            "\r\n" +
            "just text";
        var message = Load(raw);

        Assert.Equal("just text", MimeExtractor.GetDecodedTextBody(message));
        Assert.True(string.IsNullOrEmpty(MimeExtractor.GetDecodedHtmlBody(message)));
        Assert.Empty(MimeExtractor.ExtractAttachments(message));
    }
}
