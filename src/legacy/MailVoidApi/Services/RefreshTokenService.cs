using System.Security.Cryptography;
using MailVoidApi.Data;
using MailVoidApi.Models;
using RoboDodd.OrmLite;

namespace MailVoidApi.Services;

public class RefreshTokenService
{
    private readonly IDatabaseService _db;
    private readonly IConfiguration _configuration;

    public RefreshTokenService(IDatabaseService db, IConfiguration configuration)
    {
        _db = db;
        _configuration = configuration;
    }

    public async Task<RefreshToken> CreateRefreshTokenAsync(Guid userId)
    {
        var token = GenerateRefreshToken();
        var expiryDays = _configuration.GetValue<int>("JwtSettings:RefreshTokenExpiryDays", 30);

        var refreshToken = new RefreshToken
        {
            Token = token,
            UserId = userId,
            ExpiryDate = DateTime.UtcNow.AddDays(expiryDays),
            IsRevoked = false,
            CreatedDate = DateTime.UtcNow
        };

        using var db = await _db.GetConnectionAsync();
        await db.InsertAsync(refreshToken);

        return refreshToken;
    }

    public async Task<RefreshToken?> GetRefreshTokenAsync(string token)
    {
        using var db = await _db.GetConnectionAsync();
        return await db.SingleAsync<RefreshToken>(x => x.Token == token && !x.IsRevoked);
    }

    public async Task<bool> ValidateRefreshTokenAsync(string token)
    {
        var refreshToken = await GetRefreshTokenAsync(token);
        return refreshToken != null && refreshToken.ExpiryDate > DateTime.UtcNow;
    }

    public async Task RevokeRefreshTokenAsync(string token)
    {
        using var db = await _db.GetConnectionAsync();
        var refreshToken = await db.SingleAsync<RefreshToken>(x => x.Token == token);
        if (refreshToken != null)
        {
            refreshToken.IsRevoked = true;
            await db.UpdateAsync(refreshToken);
        }
    }

    public async Task RevokeAllUserRefreshTokensAsync(Guid userId)
    {
        using var db = await _db.GetConnectionAsync();
        var tokens = await db.SelectAsync<RefreshToken>(x => x.UserId == userId && !x.IsRevoked);
        foreach (var token in tokens)
        {
            token.IsRevoked = true;
            await db.UpdateAsync(token);
        }
    }

    public async Task<RefreshToken> RotateRefreshTokenAsync(string oldToken, Guid userId)
    {
        // Revoke the old token
        await RevokeRefreshTokenAsync(oldToken);

        // Create a new token
        return await CreateRefreshTokenAsync(userId);
    }

    public async Task CleanupExpiredTokensAsync()
    {
        using var db = await _db.GetConnectionAsync();
        await db.DeleteAsync<RefreshToken>(x => x.ExpiryDate < DateTime.UtcNow);
    }

    private string GenerateRefreshToken()
    {
        var randomNumber = new byte[64];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(randomNumber);
        return Convert.ToBase64String(randomNumber);
    }
}
