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
        // Debug environment variables before anything else
        Console.WriteLine($"ASPNETCORE_ENVIRONMENT from system: {Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")}");

        var host = CreateHostBuilder(args).Build();
        await host.RunAsync();
    }

    public static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .ConfigureAppConfiguration((context, config) =>
            {
                config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
                config.AddEnvironmentVariables();
                config.AddUserSecrets<Program>(optional: true);

            })
            .ConfigureServices((hostContext, services) =>
            {
                services.Configure<SmtpServerOptions>(hostContext.Configuration.GetSection("SmtpServer"));
                services.Configure<MailVoidApiOptions>(hostContext.Configuration.GetSection("MailVoidApi"));

                services.AddHttpClient<MailForwardingService>();
                services.AddTransient<IMessageStore, MailMessageStore>();
                services.AddSingleton<SmtpServerService>();
                services.AddHostedService<SmtpServerHostedService>();

                // Debug configuration loading
                var configuration = hostContext.Configuration;
                Console.WriteLine($"\n=== CONFIGURATION DEBUG ===");
                Console.WriteLine($"Environment: {hostContext.HostingEnvironment.EnvironmentName}");
                Console.WriteLine($"IsDevelopment: {hostContext.HostingEnvironment.IsDevelopment()}");
                Console.WriteLine($"UserSecretsId: d2ee9be3-64bf-42ea-b392-36fc8ec6bf45");

                // Print some config values to verify loading
                var smtpConfig = configuration.GetSection("SmtpServer");
                Console.WriteLine($"SMTP Port from config: {smtpConfig["Port"]}");
                Console.WriteLine($"SMTP SSL Port from config: {smtpConfig["SslPort"]}");

                var apiConfig = configuration.GetSection("MailVoidApi");
                Console.WriteLine($"API BaseUrl from config: {apiConfig["BaseUrl"]}");
                Console.WriteLine($"API Key from config: {(string.IsNullOrEmpty(apiConfig["ApiKey"]) ? "(empty)" : $"(loaded: {apiConfig["ApiKey"].Substring(0, Math.Min(10, apiConfig["ApiKey"].Length))}...)")}");
                Console.WriteLine($"=== END CONFIG DEBUG ===\n");
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
    public string? CertificatePath { get; set; }
    public string? CertificatePassword { get; set; }
}

public class MailVoidApiOptions
{
    public string BaseUrl { get; set; } = "http://localhost:5133";
    public string WebhookEndpoint { get; set; } = "/api/webhook/mail";
    public string ApiKey { get; set; } = "";
}
