using MailVoidSmtpServer.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
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
        Host.CreateDefaultBuilder(args)
            .ConfigureAppConfiguration((context, config) =>
            {
                config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
                config.AddEnvironmentVariables();
                config.AddUserSecrets<Program>();
            })
            .ConfigureServices((hostContext, services) =>
            {
                services.Configure<SmtpServerOptions>(hostContext.Configuration.GetSection("SmtpServer"));
                services.Configure<MailVoidApiOptions>(hostContext.Configuration.GetSection("MailVoidApi"));

                services.AddHttpClient<MailForwardingService>();
                services.AddTransient<IMessageStore, MailMessageStore>();
                services.AddSingleton<SmtpServerService>();
                services.AddHostedService<SmtpServerHostedService>();
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
    public string Name { get; set; } = "MailVoid SMTP Server";
    public int MaxMessageSize { get; set; } = 10 * 1024 * 1024; // 10MB
    public bool RequireAuthentication { get; set; } = false;
    public bool EnableSsl { get; set; } = false;
    public int SslPort { get; set; } = 465;
    public string? CertificatePath { get; set; }
    public string? CertificatePassword { get; set; }
}

public class MailVoidApiOptions
{
    public string BaseUrl { get; set; } = "http://localhost:5133";
    public string WebhookEndpoint { get; set; } = "/api/webhook/mail";
    public string ApiKey { get; set; } = "";
}
