using MailVoidApi.Data;
using MailVoidApi.Services;
using MailVoidWeb.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace MailVoidApi.Services;

public class DatabaseInitializer
{
    private readonly MailVoidDbContext _context;
    private readonly PasswordService _passwordService;

    public DatabaseInitializer(MailVoidDbContext context, PasswordService passwordService)
    {
        _context = context;
        _passwordService = passwordService;
    }

    public async Task SeedDefaultData()
    {
        // Check if admin user already exists
        var adminUser = await _context.Users.FirstOrDefaultAsync(u => u.UserName == "admin");
        if (adminUser == null)
        {
            var user = new User
            {
                Id = Guid.NewGuid(),
                UserName = "admin",
                PasswordHash = "",
                TimeStamp = DateTime.UtcNow,
                Role = Role.Admin
            };
            user.PasswordHash = _passwordService.HashPassword(user, "admin");
            
            _context.Users.Add(user);
            await _context.SaveChangesAsync();
        }
        else if (adminUser.Role != Role.Admin)
        {
            // Update existing admin user to have Admin role
            adminUser.Role = Role.Admin;
            await _context.SaveChangesAsync();
        }
    }
}
