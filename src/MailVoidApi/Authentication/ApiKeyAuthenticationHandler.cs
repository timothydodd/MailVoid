using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace MailVoidApi.Authentication;

public class ApiKeyAuthenticationHandler : AuthenticationHandler<ApiKeyAuthenticationOptions>
{
    private const string ApiKeyHeaderName = "X-Api-Key";
    private const string ApiKeyQueryName = "api_key";
    private readonly IConfiguration _configuration;

    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<ApiKeyAuthenticationOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IConfiguration configuration)
        : base(options, logger, encoder)
    {
        _configuration = configuration;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // Check header first
        if (!Request.Headers.TryGetValue(ApiKeyHeaderName, out var apiKeyFromHeader))
        {
            // Check query string as fallback
            if (!Request.Query.TryGetValue(ApiKeyQueryName, out var apiKeyFromQuery))
            {
                // Check Authorization header for "ApiKey" scheme
                var authHeader = Request.Headers["Authorization"].FirstOrDefault();
                if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("ApiKey ", StringComparison.OrdinalIgnoreCase))
                {
                    return Task.FromResult(AuthenticateResult.NoResult());
                }
                
                apiKeyFromHeader = authHeader.Substring("ApiKey ".Length).Trim();
            }
            else
            {
                apiKeyFromHeader = apiKeyFromQuery.FirstOrDefault();
            }
        }

        var providedApiKey = apiKeyFromHeader.ToString();
        var validApiKeys = _configuration.GetSection("ApiKeys").Get<List<ApiKeyConfiguration>>() ?? new List<ApiKeyConfiguration>();
        
        var matchingKey = validApiKeys.FirstOrDefault(k => k.Key == providedApiKey && k.Enabled);
        
        if (matchingKey == null)
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid API key"));
        }

        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.Name, matchingKey.Name),
            new Claim("ApiKeyName", matchingKey.Name),
            new Claim(ClaimTypes.NameIdentifier, matchingKey.Key)
        };

        // Add scopes as claims
        if (matchingKey.Scopes != null)
        {
            foreach (var scope in matchingKey.Scopes)
            {
                claims.Add(new Claim("scope", scope));
            }
        }

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}

public class ApiKeyAuthenticationOptions : AuthenticationSchemeOptions
{
}

public class ApiKeyConfiguration
{
    public string Name { get; set; } = "";
    public string Key { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public List<string>? Scopes { get; set; }
}