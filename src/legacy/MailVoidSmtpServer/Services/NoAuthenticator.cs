using System.Net;
using Microsoft.Extensions.Logging;
using SmtpServer;
using SmtpServer.Authentication;
using SmtpServer.Net;

namespace MailVoidSmtpServer.Services;

public class NoAuthenticator : IUserAuthenticator
{
    private readonly ILogger<NoAuthenticator> _logger;

    public NoAuthenticator(ILogger<NoAuthenticator> logger)
    {
        _logger = logger;
    }

    public Task<bool> AuthenticateAsync(ISessionContext context, string user, string password, CancellationToken cancellationToken)
    {
        var remote = context.Properties.TryGetValue(EndpointListener.RemoteEndPointKey, out var ep) && ep is EndPoint remoteEp
            ? remoteEp.ToString()
            : "unknown";
        var localPort = context.EndpointDefinition.Endpoint.Port;

        _logger.LogWarning(
            "[BLOCKED] Authentication attempt - User: {User}, Remote: {Remote}, LocalPort: {LocalPort}",
            user, remote, localPort);

        return Task.FromResult(false);
    }
}
