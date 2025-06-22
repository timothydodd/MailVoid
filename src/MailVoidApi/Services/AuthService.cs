using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using MailVoidWeb.Data.Models;
using Microsoft.IdentityModel.Tokens;

namespace MailVoidApi.Services;

public class AuthService
{
    private readonly IConfiguration _configuration;
    private readonly PasswordService _passwordService;

    public AuthService(IConfiguration configuration, PasswordService passwordService)
    {
        _configuration = configuration;
        _passwordService = passwordService;
    }

    public string GenerateJwtToken(User user)
    {
        var jwtSettings = _configuration.GetSection("JwtSettings");
        var secretKey = jwtSettings["Secret"] ?? throw new InvalidOperationException("JWT Secret is not configured");
        var issuer = jwtSettings["Issuer"] ?? throw new InvalidOperationException("JWT Issuer is not configured");
        var audience = jwtSettings["Audience"] ?? throw new InvalidOperationException("JWT Audience is not configured");

        // Parse expiry with validation
        if (!int.TryParse(jwtSettings["ExpiryMinutes"], out var expiryMinutes) || expiryMinutes <= 0)
        {
            throw new InvalidOperationException("JWT ExpiryMinutes must be a positive integer");
        }

        // Validate secret key length for security
        if (secretKey.Length < 32)
        {
            throw new InvalidOperationException("JWT Secret must be at least 32 characters long for security");
        }

        var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey));
        var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

        var now = DateTime.UtcNow.Subtract(new TimeSpan(0, 10, 0));
        var expiry = now.AddMinutes(expiryMinutes);

        // Create claims
        var claims = new List<Claim>
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim(JwtRegisteredClaimNames.Iat, new DateTimeOffset(now).ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()), // Add NameIdentifier as backup
            new Claim(ClaimTypes.Name, user.UserName),
            new Claim(ClaimTypes.Role, user.Role.ToString()),
        };

        // Create token using JwtSecurityToken directly for more control
        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            notBefore: now,
            expires: expiry,
            signingCredentials: credentials
        );

        var tokenHandler = new JwtSecurityTokenHandler();
        return tokenHandler.WriteToken(token);
    }

    public bool ValidateUser(User user, string password)
    {
        return _passwordService.VerifyPassword(user, password);
    }

    public ClaimsPrincipal? GetPrincipalFromExpiredToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return null;

        var jwtSettings = _configuration.GetSection("JwtSettings");
        var secretKey = jwtSettings["Secret"] ?? throw new InvalidOperationException("JWT Secret is not configured");
        var issuer = jwtSettings["Issuer"] ?? throw new InvalidOperationException("JWT Issuer is not configured");
        var audience = jwtSettings["Audience"] ?? throw new InvalidOperationException("JWT Audience is not configured");

        // Validate secret key length
        if (secretKey.Length < 32)
        {
            throw new InvalidOperationException("JWT Secret must be at least 32 characters long for security");
        }

        var tokenValidationParameters = new TokenValidationParameters
        {
            ValidateAudience = true,
            ValidAudience = audience,
            ValidateIssuer = true,
            ValidIssuer = issuer,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey)),
            ValidateLifetime = false, // We want to validate expired tokens
            ClockSkew = TimeSpan.FromMinutes(5), // Allow 5 minute clock skew
            RequireExpirationTime = true,
            RequireSignedTokens = true
        };

        var tokenHandler = new JwtSecurityTokenHandler();
        try
        {
            var principal = tokenHandler.ValidateToken(token, tokenValidationParameters, out SecurityToken validatedToken);
            var jwtToken = validatedToken as JwtSecurityToken;

            // Validate algorithm and token structure
            if (jwtToken == null ||
                !jwtToken.Header.Alg.Equals(SecurityAlgorithms.HmacSha256, StringComparison.InvariantCultureIgnoreCase) ||
                jwtToken.Header.Typ != "JWT")
            {
                return null;
            }

            return principal;
        }
        catch (SecurityTokenException)
        {
            // Log security token exceptions if needed
            return null;
        }
        catch (Exception)
        {
            // Log other exceptions if needed
            return null;
        }
    }

    public ClaimsPrincipal? ValidateToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return null;

        var jwtSettings = _configuration.GetSection("JwtSettings");
        var secretKey = jwtSettings["Secret"] ?? throw new InvalidOperationException("JWT Secret is not configured");
        var issuer = jwtSettings["Issuer"] ?? throw new InvalidOperationException("JWT Issuer is not configured");
        var audience = jwtSettings["Audience"] ?? throw new InvalidOperationException("JWT Audience is not configured");

        // Validate secret key length
        if (secretKey.Length < 32)
        {
            throw new InvalidOperationException("JWT Secret must be at least 32 characters long for security");
        }

        var tokenValidationParameters = new TokenValidationParameters
        {
            ValidateAudience = true,
            ValidAudience = audience,
            ValidateIssuer = true,
            ValidIssuer = issuer,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey)),
            ValidateLifetime = true, // Validate token is not expired
            ClockSkew = TimeSpan.FromMinutes(5), // Allow 5 minute clock skew
            RequireExpirationTime = true,
            RequireSignedTokens = true
        };

        var tokenHandler = new JwtSecurityTokenHandler();
        try
        {
            var principal = tokenHandler.ValidateToken(token, tokenValidationParameters, out SecurityToken validatedToken);
            var jwtToken = validatedToken as JwtSecurityToken;

            // Validate algorithm and token structure
            if (jwtToken == null ||
                !jwtToken.Header.Alg.Equals(SecurityAlgorithms.HmacSha256, StringComparison.InvariantCultureIgnoreCase) ||
                jwtToken.Header.Typ != "JWT")
            {
                return null;
            }

            return principal;
        }
        catch (SecurityTokenException)
        {
            // Log security token exceptions if needed
            return null;
        }
        catch (Exception)
        {
            // Log other exceptions if needed
            return null;
        }
    }

    public Guid? GetUserIdFromPrincipal(ClaimsPrincipal principal)
    {
        if (principal == null)
            return null;

        // Try multiple possible claim types for user ID
        var userIdClaim = principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value ??
                         principal.FindFirst(ClaimTypes.NameIdentifier)?.Value ??
                         principal.FindFirst("sub")?.Value;

        if (Guid.TryParse(userIdClaim, out var userId))
        {
            return userId;
        }
        return null;
    }

    public string? GetUsernameFromPrincipal(ClaimsPrincipal principal)
    {
        if (principal == null)
            return null;

        // Use standard name claim
        return principal.FindFirst(ClaimTypes.Name)?.Value;
    }

    public string? GetUserRoleFromPrincipal(ClaimsPrincipal principal)
    {
        if (principal == null)
            return null;

        // Use standard role claim
        return principal.FindFirst(ClaimTypes.Role)?.Value;
    }
}
