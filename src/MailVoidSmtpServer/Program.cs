using MailVoidSmtpServer.Services;
using MailVoidSmtpServer.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SmtpServer.Authentication;
using SmtpServer.Storage;

namespace MailVoidSmtpServer;

public class Program
{
    public static async Task Main(string[] args)
    {
        var host = CreateHostBuilder(args).Build();
        await host.RunAsync();
    }

    public static IHostBuilder CreateHostBuilder(string[] args) =>
        // Host.CreateDefaultBuilder already loads appsettings.json + appsettings.{Environment}.json
        // + user secrets (Development) + environment variables + command line, in the correct order.
        Host.CreateDefaultBuilder(args)
            .ConfigureServices((hostContext, services) =>
            {
                services.Configure<SmtpServerOptions>(hostContext.Configuration.GetSection("SmtpServer"));
                services.Configure<MailVoidApiOptions>(hostContext.Configuration.GetSection("MailVoidApi"));
                services.Configure<EmailQueueOptions>(hostContext.Configuration.GetSection("EmailQueue"));
                services.Configure<QueueMonitoringOptions>(hostContext.Configuration.GetSection("QueueMonitoring"));
                services.Configure<MailboxFilterOptions>(hostContext.Configuration.GetSection("MailboxFilter"));
                services.Configure<LetsEncryptOptions>(hostContext.Configuration.GetSection("LetsEncrypt"));

                services.AddHttpClient<MailForwardingService>(client =>
                {
                    client.Timeout = TimeSpan.FromSeconds(30);
                });
                services.AddHttpClient(); // IHttpClientFactory for DiagnosticsService
                services.AddTransient<IMessageStore, MailMessageStore>();
                services.AddSingleton<ILetsEncryptService, LetsEncryptService>();
                services.AddSingleton<ICertificateService, CertificateService>();
                services.AddSingleton<SmtpServerService>();
                
                // Security services
                services.AddSingleton<MailVoidMailboxFilter>();
                services.AddSingleton<IMailboxFilter>(provider => provider.GetRequiredService<MailVoidMailboxFilter>());
                services.AddSingleton<IMailboxFilterFactory>(provider => provider.GetRequiredService<MailVoidMailboxFilter>());

                services.AddSingleton<NoAuthenticator>();
                services.AddSingleton<IUserAuthenticator>(provider => provider.GetRequiredService<NoAuthenticator>());

                // Single durable, disk-backed queue.
                services.AddSingleton<IEmailQueueService, EmailSpoolQueue>();

                // Background services. Let's Encrypt is registered first so cert acquisition kicks off
                // early; it runs in the background and re-binds the SMTP server once a cert is available.
                services.AddHostedService<LetsEncryptHostedService>();
                services.AddHostedService<SmtpServerHostedService>();
                services.AddHostedService<EmailProcessorService>();
                services.AddHostedService<QueueMonitoringService>();
                services.AddHostedService<DiagnosticsService>();
            })
            .ConfigureLogging((context, logging) =>
            {
                logging.ClearProviders();
                logging.AddConsole();
                logging.AddDebug();

                // Only add EventLog on Windows
                if (OperatingSystem.IsWindows())
                {
                    logging.AddEventLog();
                }

                // Configure log levels from appsettings.json
                logging.AddConfiguration(context.Configuration.GetSection("Logging"));

                // Add structured logging with timestamp and category
                logging.Configure(options =>
                {
                    options.ActivityTrackingOptions = ActivityTrackingOptions.SpanId
                                                    | ActivityTrackingOptions.TraceId
                                                    | ActivityTrackingOptions.ParentId;
                });
            });
}

public class SmtpServerOptions
{
    public int Port { get; set; } = 25;
    public int TestPort { get; set; } = 2580;
    public int SslPort { get; set; } = 465;  // Implicit TLS/SSL
    public int StartTlsPort { get; set; } = 587;  // STARTTLS
    public string Name { get; set; } = "MailVoid SMTP Server";
    public int MaxMessageSize { get; set; } = 10 * 1024 * 1024; // 10MB
    public string? CertificatePath { get; set; }
    public string? CertificatePassword { get; set; }
    public bool EnableSsl { get; set; } = false;
    public string TlsProtocols { get; set; } = "Tls12,Tls13";  // Default to TLS 1.2 and 1.3
    public string? LetsEncryptDomain { get; set; }  // Primary domain for Let's Encrypt integration
}

public class MailVoidApiOptions
{
    public List<MailVoidApiTarget> Targets { get; set; } = new();
}

public class MailVoidApiTarget
{
    public string Name { get; set; } = "";
    public string BaseUrl { get; set; } = "";
    public string WebhookEndpoint { get; set; } = "/api/webhook/mail";
    public string ApiKey { get; set; } = "";
    public bool Enabled { get; set; } = true;
}
