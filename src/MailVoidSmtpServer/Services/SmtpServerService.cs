using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmtpServer;
using SmtpServer.ComponentModel;

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

        var optionsBuilder = new SmtpServerOptionsBuilder()
            .ServerName(_options.Name)
            .MaxMessageSize(_options.MaxMessageSize);

        // Configure non-SSL endpoint
        optionsBuilder.Endpoint(builder =>
            builder
                .Port(_options.Port)
                .AllowUnsecureAuthentication(true));

        // Configure SSL/TLS if enabled
        if (_options.EnableSsl)
        {
            _logger.LogInformation("Configuring SSL/TLS on port {SslPort}", _options.SslPort);

            if (string.IsNullOrEmpty(_options.CertificatePath))
            {
                _logger.LogWarning("SSL is enabled but no certificate path provided. Generating self-signed certificate.");
                
                // Generate a self-signed certificate for development
                var certificate = GenerateSelfSignedCertificate(_options.Name);
                
                optionsBuilder.Endpoint(builder =>
                {
                    builder
                        .Port(_options.SslPort, true)
                        .AllowUnsecureAuthentication(false);

                    // Enable SSL/TLS protocols
                    builder.SupportedSslProtocols(SslProtocols.Tls12 | SslProtocols.Tls13);
                    builder.Certificate(certificate);
                });
                
                _logger.LogInformation("Self-signed certificate generated successfully for {Name}", _options.Name);
            }
            else
            {
                try
                {
                    // Use X509CertificateLoader for .NET 9
                    var certificate = string.IsNullOrEmpty(_options.CertificatePassword)
                        ? X509CertificateLoader.LoadCertificateFromFile(_options.CertificatePath)
                        : X509CertificateLoader.LoadPkcs12FromFile(_options.CertificatePath, _options.CertificatePassword);
                    // In SmtpServer v11, certificate is configured through the Endpoint builder
                    // SmtpServer v11 requires certificate to be set differently
                    optionsBuilder.Endpoint(builder =>
                    {
                        builder
                            .Port(_options.SslPort, true)
                            .AllowUnsecureAuthentication(false);

                        // Set the certificate via the SslServerAuthenticationOptions
                        builder.SupportedSslProtocols(SslProtocols.Tls12 | SslProtocols.Tls13);
                        builder.Certificate(certificate);
                    });
                    _logger.LogInformation("SSL certificate loaded successfully from {Path}", _options.CertificatePath);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to load SSL certificate from {Path}", _options.CertificatePath);
                    throw;
                }
            }
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

        var ports = _options.EnableSsl ? $"{_options.Port} and SSL on {_options.SslPort}" : _options.Port.ToString();
        _logger.LogInformation("SMTP server started successfully on port(s) {Ports}", ports);
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
        _logger.LogInformation("SMTP session created - SessionId: {SessionId}, RemoteEndPoint: {RemoteEndPoint}",
            e.Context.SessionId,
            e.Context.EndpointDefinition.Endpoint);
    }

    private void OnSessionCompleted(object? sender, SessionEventArgs e)
    {
        _logger.LogInformation("SMTP session completed - SessionId: {SessionId}, RemoteEndPoint: {RemoteEndPoint}",
            e.Context.SessionId,
            e.Context.EndpointDefinition.Endpoint);
    }

    private void OnSessionFaulted(object? sender, SessionFaultedEventArgs e)
    {
        _logger.LogError(e.Exception, "SMTP session faulted - SessionId: {SessionId}, RemoteEndPoint: {RemoteEndPoint}",
            e.Context.SessionId,
            e.Context.EndpointDefinition.Endpoint);
    }

    private void OnSessionCancelled(object? sender, SessionEventArgs e)
    {
        _logger.LogWarning("SMTP session cancelled - SessionId: {SessionId}, RemoteEndPoint: {RemoteEndPoint}",
            e.Context.SessionId,
            e.Context.EndpointDefinition.Endpoint);
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
