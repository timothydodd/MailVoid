using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmtpServer;
using System.Security.Authentication;

namespace MailVoidSmtpServer.Services;
public class SmtpServerService
{
    private readonly ILogger<SmtpServerService> _logger;
    private readonly SmtpServerOptions _options;
    private readonly IServiceProvider _serviceProvider;
    private readonly ICertificateService _certificateService;
    private SmtpServer.SmtpServer? _server;

    public SmtpServerService(
        ILogger<SmtpServerService> logger,
        IOptions<SmtpServerOptions> options,
        IServiceProvider serviceProvider,
        ICertificateService certificateService)
    {
        _logger = logger;
        _options = options.Value;
        _serviceProvider = serviceProvider;
        _certificateService = certificateService;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var ports = new List<string> { _options.Port.ToString(), _options.TestPort.ToString() };
        var certificate = _certificateService.GetCertificate();
        var sslEnabled = _options.EnableSsl && certificate != null;

        if (sslEnabled)
        {
            ports.Add($"{_options.SslPort} (SSL)");
            ports.Add($"{_options.StartTlsPort} (STARTTLS)");
        }

        _logger.LogInformation("Starting SMTP server on ports {Ports} with mailbox filtering enabled", 
            string.Join(", ", ports));

        var optionsBuilder = new SmtpServerOptionsBuilder()
            .ServerName(_options.Name)
            .MaxMessageSize(_options.MaxMessageSize);

        // Plain text endpoint (port 25)
        optionsBuilder.Endpoint((builder) =>
        {
            builder.Port(_options.Port, isSecure: false)
                .AllowUnsecureAuthentication(true)
                .AuthenticationRequired(false);
        });

        // Test port endpoint (plain text)
        optionsBuilder.Endpoint((builder) =>
        {
            builder.Port(_options.TestPort, isSecure: false)
                .AllowUnsecureAuthentication(true)
                .AuthenticationRequired(false);
        });

        // Add SSL/TLS endpoints if certificate is available
        if (sslEnabled && certificate != null)
        {
            // Parse TLS protocols
            var tlsProtocols = ParseTlsProtocols(_options.TlsProtocols);

            // Implicit SSL/TLS endpoint (port 465)
            optionsBuilder.Endpoint((builder) =>
            {
                builder.Port(_options.SslPort, isSecure: true)
                    .Certificate(certificate)
                    .SupportedSslProtocols(tlsProtocols)
                    .AllowUnsecureAuthentication(false)
                    .AuthenticationRequired(false);
            });

            // STARTTLS endpoint (port 587)
            optionsBuilder.Endpoint((builder) =>
            {
                builder.Port(_options.StartTlsPort, isSecure: false)
                    .Certificate(certificate)
                    .SupportedSslProtocols(tlsProtocols)
                    .AllowUnsecureAuthentication(false)
                    .AuthenticationRequired(false)
                    .IsSecure(false); // STARTTLS starts unencrypted
            });

            _logger.LogInformation("SSL/TLS enabled with certificate: {Subject}, TLS Protocols: {Protocols}", 
                certificate.Subject, _options.TlsProtocols);
        }
        else if (_options.EnableSsl && certificate == null)
        {
            _logger.LogWarning("SSL is enabled in configuration but no valid certificate was loaded. SSL endpoints will not be available.");
        }

        var options = optionsBuilder.Build();
        _server = new SmtpServer.SmtpServer(options, _serviceProvider);

        // Subscribe to server events for logging
        _server.SessionCreated += OnSessionCreated;
        _server.SessionCompleted += OnSessionCompleted;
        _server.SessionFaulted += OnSessionFaulted;
        _server.SessionCancelled += OnSessionCancelled;

        _ = Task.Run(async () =>
        {
            try
            {
                await _server.StartAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SMTP server error");
            }
        }, cancellationToken);

        _logger.LogInformation("SMTP server started successfully on ports {Ports} with authentication: {AuthRequired}, MailboxFilter: Enabled, SSL: {SslEnabled}",
            string.Join(", ", ports), false, sslEnabled);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping SMTP server");

        if (_server != null)
        {
            _server.Shutdown();
            await Task.CompletedTask;
        }

        _logger.LogInformation("SMTP server stopped");
    }

    private void OnSessionCreated(object? sender, SessionEventArgs e)
    {
        var isSecure = e.Context.EndpointDefinition.IsSecure;
        var securityInfo = isSecure ? "SSL/TLS" : "Plain Text";
        var endpoint = e.Context.EndpointDefinition.Endpoint;
        var portInfo = endpoint?.Port.ToString() ?? "unknown";

        _logger.LogInformation("SMTP session created - SessionId: {SessionId}, RemoteEndPoint: {RemoteEndPoint}, Port: {Port}, Security: {Security}",
            e.Context.SessionId,
            endpoint,
            portInfo,
            securityInfo);
    }

    private void OnSessionCompleted(object? sender, SessionEventArgs e)
    {
        var isSecure = e.Context.EndpointDefinition.IsSecure;
        var securityInfo = isSecure ? "SSL/TLS" : "Plain Text";

        _logger.LogInformation("SMTP session completed - SessionId: {SessionId}, RemoteEndPoint: {RemoteEndPoint}, Security: {Security}",
            e.Context.SessionId,
            e.Context.EndpointDefinition.Endpoint,
            securityInfo);
    }

    private void OnSessionFaulted(object? sender, SessionFaultedEventArgs e)
    {
        var isSecure = e.Context.EndpointDefinition.IsSecure;
        var securityInfo = isSecure ? "SSL/TLS" : "Plain Text";

        _logger.LogError(e.Exception, "SMTP session faulted - SessionId: {SessionId}, RemoteEndPoint: {RemoteEndPoint}, Security: {Security}",
            e.Context.SessionId,
            e.Context.EndpointDefinition.Endpoint,
            securityInfo);
    }

    private void OnSessionCancelled(object? sender, SessionEventArgs e)
    {
        var isSecure = e.Context.EndpointDefinition.IsSecure;
        var securityInfo = isSecure ? "SSL/TLS" : "Plain Text";

        _logger.LogWarning("SMTP session cancelled - SessionId: {SessionId}, RemoteEndPoint: {RemoteEndPoint}, Security: {Security}",
            e.Context.SessionId,
            e.Context.EndpointDefinition.Endpoint,
            securityInfo);
    }

    private SslProtocols ParseTlsProtocols(string protocols)
    {
        var result = SslProtocols.None;
        
        if (string.IsNullOrWhiteSpace(protocols))
        {
            return SslProtocols.Tls12 | SslProtocols.Tls13;  // Default to TLS 1.2 and 1.3
        }

        var parts = protocols.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var part in parts)
        {
            if (Enum.TryParse<SslProtocols>(part, true, out var protocol))
            {
                result |= protocol;
            }
            else
            {
                _logger.LogWarning("Unknown TLS protocol: {Protocol}", part);
            }
        }

        if (result == SslProtocols.None)
        {
            _logger.LogWarning("No valid TLS protocols specified. Falling back to TLS 1.2 and 1.3");
            return SslProtocols.Tls12 | SslProtocols.Tls13;
        }

        return result;
    }
}

public class SmtpServerHostedService : IHostedService
{
    private readonly SmtpServerService _smtpServerService;

    public SmtpServerHostedService(SmtpServerService smtpServerService)
    {
        _smtpServerService = smtpServerService;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        return _smtpServerService.StartAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return _smtpServerService.StopAsync(cancellationToken);
    }
}
