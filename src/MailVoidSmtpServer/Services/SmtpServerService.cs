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
            .Endpoint((builder) =>
            {
                builder.Port(25, isSecure: false)
                .AllowUnsecureAuthentication(true)   // Allow plain text for port 25
                .AuthenticationRequired(false);      // Optional auth for relay
                /certificate for plain text only
            })





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



        _logger.LogInformation("SMTP server started successfully on port 25 (plain text only)");
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
