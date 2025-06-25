using SmtpServer;
using SmtpServer.Authentication;

namespace MailVoidSmtpServer.Services;

public class NoAuthenticator : IUserAuthenticator
{
    public Task<bool> AuthenticateAsync(ISessionContext context, string user, string password, CancellationToken cancellationToken)
    {
        // Log the attempt for security monitoring
        Console.WriteLine($"[BLOCKED] Authentication attempt - User: {user}, IP: {context.EndpointDefinition.Endpoint}");
        
        // Always deny authentication
        return Task.FromResult(false);
    }
}