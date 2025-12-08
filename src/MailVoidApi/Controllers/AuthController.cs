using MailVoidApi.Data;
using MailVoidApi.Services;
using MailVoidWeb.Data.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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

    public AuthController(IDatabaseService db, AuthService authService, PasswordService passwordService, RefreshTokenService refreshTokenService, IConfiguration configuration)
    {
        _db = db;
        _authService = authService;
        _passwordService = passwordService;
        _refreshTokenService = refreshTokenService;
        _configuration = configuration;
    }
    [AllowAnonymous]
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

        return Ok(new LoginResponse
        {
            AccessToken = token,
            RefreshToken = refreshToken.Token,
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

        if (!await _refreshTokenService.ValidateRefreshTokenAsync(request.RefreshToken))
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
        var newRefreshToken = await _refreshTokenService.RotateRefreshTokenAsync(request.RefreshToken, user.Id);
        var newAccessToken = _authService.GenerateJwtToken(user);

        return Ok(new LoginResponse
        {
            AccessToken = newAccessToken,
            RefreshToken = newRefreshToken.Token,
            ExpiresIn = _configuration.GetValue<int>("JwtSettings:ExpiryMinutes", 30) * 60
        });
    }

    [HttpPost("revoke")]
    public async Task<IActionResult> RevokeToken([FromBody] RevokeTokenRequest request)
    {
        await _refreshTokenService.RevokeRefreshTokenAsync(request.RefreshToken);
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
    public required string RefreshToken { get; set; }
    public int ExpiresIn { get; set; }
}

public class RefreshTokenRequest
{
    public required string AccessToken { get; set; }
    public required string RefreshToken { get; set; }
}

public class RevokeTokenRequest
{
    public required string RefreshToken { get; set; }
}
