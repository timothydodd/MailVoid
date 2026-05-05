using Microsoft.Extensions.Logging;
using SmtpServer;
using SmtpServer.Authentication;

namespace MailVoidSmtpServer.Services;

public class NoAuthenticator : IUserAuthenticator
{
    private readonly ILogger<NoAuthenticator> _logger;
    private readonly IIpBlacklistService _blacklist;

    public NoAuthenticator(ILogger<NoAuthenticator> logger, IIpBlacklistService blacklist)
    {
        _logger = logger;
        _blacklist = blacklist;
    }

    public Task<bool> AuthenticateAsync(ISessionContext context, string user, string password, CancellationToken cancellationToken)
    {
        var remote = _blacklist.GetRemoteIp(context) ?? "unknown";
        var localPort = context.EndpointDefinition.Endpoint.Port;

        _logger.LogWarning(
            "[BLOCKED] Authentication attempt - User: {User}, Remote: {Remote}, LocalPort: {LocalPort}",
            user, remote, localPort);

        if (remote != "unknown")
        {
            _blacklist.Add(remote, $"AUTH attempt as '{user}' on port {localPort}");
        }

        return Task.FromResult(false);
    }
}
