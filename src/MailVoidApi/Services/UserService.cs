using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;

namespace MailVoidApi.Services;

public class UserService : IUserService
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public UserService(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public Guid GetUserId()
    {
        var httpContext = _httpContextAccessor.HttpContext;

        // Ensure HttpContext and User are available
        if (httpContext?.User?.Identity is not ClaimsIdentity identity)
            throw new UnauthorizedAccessException("User not authenticated");

        // Extract the User ID claim from standard 'sub' claim or NameIdentifier as backup
        var userIdClaim = identity.FindFirst(JwtRegisteredClaimNames.Sub) ?? 
                         identity.FindFirst(ClaimTypes.NameIdentifier) ??
                         identity.FindFirst("sub");
        
        userIdClaim.ThrowIfNull("Invalid User Id");

        return Guid.Parse(userIdClaim.Value);
    }

}
public interface IUserService
{
    Guid GetUserId();
}
