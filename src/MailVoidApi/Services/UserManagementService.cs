using MailVoidApi.Data;
using MailVoidWeb.Data.Models;
using RoboDodd.OrmLite;

namespace MailVoidApi.Services;

public class UserManagementService
{
    private readonly IDatabaseService _db;
    private readonly PasswordService _passwordService;
    private readonly IMailGroupService _mailGroupService;

    public UserManagementService(IDatabaseService db, PasswordService passwordService, IMailGroupService mailGroupService)
    {
        _db = db;
        _passwordService = passwordService;
        _mailGroupService = mailGroupService;
    }

    public async Task<List<User>> GetAllUsersAsync()
    {
        using var db = await _db.GetConnectionAsync();
        return await db.SelectAsync<User>();
    }

    public async Task<User?> GetUserByIdAsync(Guid userId)
    {
        using var db = await _db.GetConnectionAsync();
        return await db.SingleByIdAsync<User>(userId);
    }

    public async Task<User?> CreateUserAsync(string userName, string password, Role role = Role.User)
    {
        using var db = await _db.GetConnectionAsync();

        // Check if username already exists
        var existingUser = await db.SingleAsync<User>(u => u.UserName == userName);

        if (existingUser != null)
        {
            return null; // Username already exists
        }

        var user = new User
        {
            Id = Guid.NewGuid(),
            UserName = userName,
            PasswordHash = "", // Will be set below
            TimeStamp = DateTime.UtcNow,
            Role = role
        };

        // Hash the password
        user.PasswordHash = _passwordService.HashPassword(user, password);

        await db.InsertAsync(user);

        // Create a default private MailGroup for the user
        await _mailGroupService.CreateUserPrivateMailGroup(user.Id, isDefault: true);

        return user;
    }

    public async Task<bool> UpdateUserRoleAsync(Guid userId, Role role)
    {
        using var db = await _db.GetConnectionAsync();

        var user = await db.SingleByIdAsync<User>(userId);

        if (user == null)
        {
            return false;
        }

        user.Role = role;
        await db.UpdateAsync(user);
        return true;
    }

    public async Task<bool> UpdateUserPasswordAsync(Guid userId, string newPassword)
    {
        using var db = await _db.GetConnectionAsync();

        var user = await db.SingleByIdAsync<User>(userId);

        if (user == null)
        {
            return false;
        }

        user.PasswordHash = _passwordService.HashPassword(user, newPassword);
        await db.UpdateAsync(user);
        return true;
    }

    public async Task<bool> DeleteUserAsync(Guid userId)
    {
        using var db = await _db.GetConnectionAsync();

        var user = await db.SingleByIdAsync<User>(userId);

        if (user == null)
        {
            return false;
        }

        await db.DeleteAsync(user);
        return true;
    }

    public async Task<bool> UserExistsAsync(string userName)
    {
        using var db = await _db.GetConnectionAsync();
        return await db.ExistsAsync<User>(u => u.UserName == userName);
    }
}