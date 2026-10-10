using MailVoidApi.Data;
using MailVoidApi.Services;
using MailVoidWeb.Data.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using RoboDodd.OrmLite;

namespace MailVoidApi.Controllers;
[Authorize]
[ApiController]
[Route("api/auth")]
public class AuthController : Controller
{
    private readonly IDatabaseService _db;
    private readonly AuthService _authService;
    private readonly PasswordService _passwordService;
    private readonly RefreshTokenService _refreshTokenService;
    private readonly IConfiguration _configuration;

    // The refresh token is delivered as an httpOnly cookie so it is never readable
    // from JavaScript (XSS-safe). Scoped to /api/auth so it only rides along on the
    // refresh/logout/revoke calls, not on every API request.
    private const string RefreshTokenCookieName = "refreshToken";
    private const string RefreshTokenCookiePath = "/api/auth";

    public AuthController(IDatabaseService db, AuthService authService, PasswordService passwordService, RefreshTokenService refreshTokenService, IConfiguration configuration)
    {
        _db = db;
        _authService = authService;
        _passwordService = passwordService;
        _refreshTokenService = refreshTokenService;
        _configuration = configuration;
    }

    private CookieOptions BuildRefreshCookieOptions(DateTimeOffset expires)
    {
        // Defaults suit a cross-origin SPA over HTTPS (SameSite=None requires Secure).
        // For a same-origin deployment set JwtSettings:RefreshTokenCookieSameSite=Lax.
        var sameSiteSetting = _configuration.GetValue<string>("JwtSettings:RefreshTokenCookieSameSite") ?? "None";
        var sameSite = Enum.TryParse<SameSiteMode>(sameSiteSetting, ignoreCase: true, out var parsed) ? parsed : SameSiteMode.None;
        var secure = _configuration.GetValue<bool?>("JwtSettings:RefreshTokenCookieSecure") ?? true;
        return new CookieOptions
        {
            HttpOnly = true,
            Secure = secure,
            SameSite = sameSite,
            Path = RefreshTokenCookiePath,
            Expires = expires,
            IsEssential = true
        };
    }

    private void SetRefreshTokenCookie(string token)
    {
        var expiryDays = _configuration.GetValue<int>("JwtSettings:RefreshTokenExpiryDays", 30);
        Response.Cookies.Append(RefreshTokenCookieName, token, BuildRefreshCookieOptions(DateTimeOffset.UtcNow.AddDays(expiryDays)));
    }

    private void DeleteRefreshTokenCookie()
    {
        Response.Cookies.Append(RefreshTokenCookieName, string.Empty, BuildRefreshCookieOptions(DateTimeOffset.UnixEpoch));
    }
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        using var db = await _db.GetConnectionAsync();
        var user = await db.SingleAsync<User>(u => u.UserName == request.UserName);
        if (user == null || !_authService.ValidateUser(user, request.Password))
        {
            return Unauthorized("Invalid email or password.");
        }

        var token = _authService.GenerateJwtToken(user);
        var refreshToken = await _refreshTokenService.CreateRefreshTokenAsync(user.Id);
        SetRefreshTokenCookie(refreshToken.Token);

        return Ok(new LoginResponse
        {
            AccessToken = token,
            ExpiresIn = _configuration.GetValue<int>("JwtSettings:ExpiryMinutes", 30) * 60
        });
    }
    [Authorize]
    [HttpGet("user")]
    public async Task<IActionResult> GetUser()
    {
        var userId = _authService.GetUserIdFromPrincipal(User);
        if (userId == null)
        {
            return Unauthorized();
        }

        using var db = await _db.GetConnectionAsync();
        var user = await db.SingleByIdAsync<User>(userId.Value);
        if (user == null)
        {
            return NotFound();
        }

        return Ok(new UserResponse() { Id = user.Id, UserName = user.UserName });
    }
    [Authorize]
    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePasswordAsync([FromBody] ChangePasswordRequest request)
    {
        var userId = _authService.GetUserIdFromPrincipal(User);
        if (userId == null)
        {
            return Unauthorized();
        }

        using var db = await _db.GetConnectionAsync();
        var user = await db.SingleByIdAsync<User>(userId.Value);
        if (user == null)
        {
            return NotFound();
        }
        if (!_authService.ValidateUser(user, request.OldPassword))
        {
            return Unauthorized("Invalid password.");
        }
        user.PasswordHash = _passwordService.HashPassword(user, request.NewPassword);
        await db.UpdateAsync(user);
        return Ok();
    }

    [AllowAnonymous]
    [HttpPost("refresh")]
    public async Task<IActionResult> RefreshToken([FromBody] RefreshTokenRequest request)
    {
        var principal = _authService.GetPrincipalFromExpiredToken(request.AccessToken);
        if (principal == null)
        {
            return BadRequest("Invalid access token");
        }

        var userId = _authService.GetUserIdFromPrincipal(principal);
        if (!userId.HasValue)
        {
            return BadRequest("Invalid access token");
        }

        // Prefer the httpOnly cookie; fall back to the request body for transitional
        // clients that still send the token inline.
        var refreshTokenValue = Request.Cookies[RefreshTokenCookieName];
        if (string.IsNullOrEmpty(refreshTokenValue))
        {
            refreshTokenValue = request.RefreshToken;
        }
        if (string.IsNullOrEmpty(refreshTokenValue))
        {
            return Unauthorized("Invalid refresh token");
        }

        // GetRefreshTokenAsync only returns non-revoked tokens, so a previously used/rotated
        // (and therefore revoked) token cannot be reused here.
        var storedToken = await _refreshTokenService.GetRefreshTokenAsync(refreshTokenValue);
        if (storedToken == null || storedToken.ExpiryDate <= DateTime.UtcNow)
        {
            return Unauthorized("Invalid refresh token");
        }

        // Bind the refresh token to the user identified by the (expired) access token.
        if (storedToken.UserId != userId.Value)
        {
            return Unauthorized("Invalid refresh token");
        }

        using var db = await _db.GetConnectionAsync();
        var user = await db.SingleByIdAsync<User>(userId.Value);
        if (user == null)
        {
            return NotFound("User not found");
        }

        // Rotate refresh token
        var newRefreshToken = await _refreshTokenService.RotateRefreshTokenAsync(refreshTokenValue, user.Id);
        var newAccessToken = _authService.GenerateJwtToken(user);
        SetRefreshTokenCookie(newRefreshToken.Token);

        return Ok(new LoginResponse
        {
            AccessToken = newAccessToken,
            ExpiresIn = _configuration.GetValue<int>("JwtSettings:ExpiryMinutes", 30) * 60
        });
    }

    [HttpPost("revoke")]
    public async Task<IActionResult> RevokeToken([FromBody] RevokeTokenRequest request)
    {
        var userId = _authService.GetUserIdFromPrincipal(User);
        if (userId == null)
        {
            return Unauthorized();
        }

        var refreshTokenValue = Request.Cookies[RefreshTokenCookieName];
        if (string.IsNullOrEmpty(refreshTokenValue))
        {
            refreshTokenValue = request.RefreshToken;
        }
        if (string.IsNullOrEmpty(refreshTokenValue))
        {
            return BadRequest("No refresh token supplied");
        }

        // Only allow a user to revoke their own refresh tokens.
        var storedToken = await _refreshTokenService.GetRefreshTokenAsync(refreshTokenValue);
        if (storedToken != null && storedToken.UserId != userId.Value)
        {
            return Forbid();
        }

        await _refreshTokenService.RevokeRefreshTokenAsync(refreshTokenValue);
        DeleteRefreshTokenCookie();
        return Ok();
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        var userId = _authService.GetUserIdFromPrincipal(User);
        if (userId.HasValue)
        {
            await _refreshTokenService.RevokeAllUserRefreshTokensAsync(userId.Value);
        }
        DeleteRefreshTokenCookie();
        return Ok();
    }

}
public class ChangePasswordRequest
{
    public required string OldPassword { get; set; }
    public required string NewPassword { get; set; }
}
public class LoginRequest
{
    public required string UserName { get; set; }
    public required string Password { get; set; }
}
public class UserResponse
{
    public Guid Id { get; set; }
    public required string UserName { get; set; }
}

public class LoginResponse
{
    public required string AccessToken { get; set; }
    public int ExpiresIn { get; set; }
}

public class RefreshTokenRequest
{
    public required string AccessToken { get; set; }
    // Refresh token now arrives via the httpOnly cookie; kept optional for transitional clients.
    public string? RefreshToken { get; set; }
}

public class RevokeTokenRequest
{
    // Optional: the httpOnly cookie is used when present.
    public string? RefreshToken { get; set; }
}
