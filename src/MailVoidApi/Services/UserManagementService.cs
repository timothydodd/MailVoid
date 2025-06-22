using MailVoidApi.Data;
using MailVoidWeb.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace MailVoidApi.Services;

public class UserManagementService
{
    private readonly MailVoidDbContext _context;
    private readonly PasswordService _passwordService;

    public UserManagementService(MailVoidDbContext context, PasswordService passwordService)
    {
        _context = context;
        _passwordService = passwordService;
    }

    public async Task<List<User>> GetAllUsersAsync()
    {
        return await _context.Users
            .OrderBy(u => u.UserName)
            .ToListAsync();
    }

    public async Task<User?> GetUserByIdAsync(Guid userId)
    {
        return await _context.Users
            .FirstOrDefaultAsync(u => u.Id == userId);
    }

    public async Task<User?> CreateUserAsync(string userName, string password, Role role = Role.User)
    {
        // Check if username already exists
        var existingUser = await _context.Users
            .FirstOrDefaultAsync(u => u.UserName == userName);
        
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

        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        return user;
    }

    public async Task<bool> UpdateUserRoleAsync(Guid userId, Role role)
    {
        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Id == userId);
        
        if (user == null)
        {
            return false;
        }

        user.Role = role;
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> UpdateUserPasswordAsync(Guid userId, string newPassword)
    {
        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Id == userId);
        
        if (user == null)
        {
            return false;
        }

        user.PasswordHash = _passwordService.HashPassword(user, newPassword);
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteUserAsync(Guid userId)
    {
        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Id == userId);
        
        if (user == null)
        {
            return false;
        }

        _context.Users.Remove(user);
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> UserExistsAsync(string userName)
    {
        return await _context.Users
            .AnyAsync(u => u.UserName == userName);
    }
}