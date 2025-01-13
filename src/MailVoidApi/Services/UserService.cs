using System.Security.Claims;


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

        // Extract the User ID claim (commonly "sub" or "userId")
        var userIdClaim = identity.FindFirst("userId"); // Adjust as per your JWT claim setup
        userIdClaim.ThrowIfNull("Invalid User Id");

        return Guid.Parse(userIdClaim.Value);
    }

}
public interface IUserService
{
    Guid GetUserId();
}
