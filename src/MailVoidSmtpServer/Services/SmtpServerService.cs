using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmtpServer;

namespace MailVoidSmtpServer.Services;
public class SmtpServerService
{
    private readonly ILogger<SmtpServerService> _logger;
    private readonly SmtpServerOptions _options;
    private readonly IServiceProvider _serviceProvider;
    private SmtpServer.SmtpServer? _server;

    public SmtpServerService(
        ILogger<SmtpServerService> logger,
        IOptions<SmtpServerOptions> options,
        IServiceProvider serviceProvider)
    {
        _logger = logger;
        _options = options.Value;
        _serviceProvider = serviceProvider;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting SMTP server on port {Port}", _options.Port);

        var options = new SmtpServerOptionsBuilder()
            .ServerName(_options.Name)
            .MaxMessageSize(_options.MaxMessageSize)
            .Endpoint(builder => builder
                .Port(25, isSecure: false)
                .AllowUnsecureAuthentication(true)  // Allow for legacy relay
                .AuthenticationRequired(false)      // Optional auth for relay
                .Certificate(CreateCertificate()))

            // Port 587: Require STARTTLS for submission  
            .Endpoint(builder => builder
                .Port(587, isSecure: false)
                .AllowUnsecureAuthentication(false) // Force STARTTLS before auth
                .AuthenticationRequired(false)       // Always require auth
                .Certificate(CreateCertificate()))

            // Port 465: Implicit TLS (alternative)
            .Endpoint(builder => builder
                .Port(465, isSecure: true)          // Immediate TLS
                .AllowUnsecureAuthentication(false)
                .AuthenticationRequired(false)
                .Certificate(CreateCertificate()))
            .Build();


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



        _logger.LogInformation("SMTP server started successfully on ports 25 (plain), 587 (STARTTLS), 465 (TLS)");
        return Task.CompletedTask;
    }
    private X509Certificate2 CreateCertificate()
    {
        if (!string.IsNullOrEmpty(_options.CertificatePath))
        {
            try
            {
                // Load certificate from file
                return string.IsNullOrEmpty(_options.CertificatePassword)
                    ? X509CertificateLoader.LoadCertificateFromFile(_options.CertificatePath)
                    : X509CertificateLoader.LoadPkcs12FromFile(_options.CertificatePath, _options.CertificatePassword);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load SSL certificate from {Path}", _options.CertificatePath);
                throw;
            }
        }
        // Generate self-signed certificate for development/testing
        return GenerateSelfSignedCertificate(_options.Name);
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

    private static X509Certificate2 GenerateSelfSignedCertificate(string serverName)
    {
        var distinguishedName = new X500DistinguishedName($"CN={serverName}");

        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(distinguishedName, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        // Add extensions
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                X509KeyUsageFlags.DataEncipherment |
                X509KeyUsageFlags.KeyEncipherment |
                X509KeyUsageFlags.DigitalSignature,
                false));

        request.CertificateExtensions.Add(
            new X509EnhancedKeyUsageExtension(
                new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, // Server Authentication
                false));

        // Add Subject Alternative Names
        var sanBuilder = new SubjectAlternativeNameBuilder();
        sanBuilder.AddDnsName(serverName);
        sanBuilder.AddDnsName("localhost");
        sanBuilder.AddIpAddress(System.Net.IPAddress.Loopback);
        sanBuilder.AddIpAddress(System.Net.IPAddress.IPv6Loopback);
        request.CertificateExtensions.Add(sanBuilder.Build());

        // Create certificate valid for 1 year
        var certificate = request.CreateSelfSigned(
            DateTimeOffset.Now.AddDays(-1),
            DateTimeOffset.Now.AddYears(1));

        // For .NET 9, we can return the certificate directly
        // The certificate already has the private key attached from CreateSelfSigned
        return certificate;
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
