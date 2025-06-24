using MailVoidApi.Data;
using MailVoidApi.Services;
using MailVoidWeb.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace MailVoidApi.Services;

public class DatabaseInitializer
{
    private readonly MailVoidDbContext _context;
    private readonly PasswordService _passwordService;
    private readonly IMailGroupService _mailGroupService;

    public DatabaseInitializer(MailVoidDbContext context, PasswordService passwordService, IMailGroupService mailGroupService)
    {
        _context = context;
        _passwordService = passwordService;
        _mailGroupService = mailGroupService;
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
            
            // Create a default private MailGroup for admin user
            await _mailGroupService.CreateUserPrivateMailGroup(user.Id, isDefault: true);
        }
        else if (adminUser.Role != Role.Admin)
        {
            // Update existing admin user to have Admin role
            adminUser.Role = Role.Admin;
            await _context.SaveChangesAsync();
        }
        
        // Ensure admin user has a private MailGroup (get the user again to ensure we have the right reference)
        var currentAdminUser = await _context.Users.FirstOrDefaultAsync(u => u.UserName == "admin");
        if (currentAdminUser != null)
        {
            var adminPrivateGroups = await _mailGroupService.GetUserPrivateMailGroups(currentAdminUser.Id);
            if (!adminPrivateGroups.Any())
            {
                await _mailGroupService.CreateUserPrivateMailGroup(currentAdminUser.Id, isDefault: true);
            }
        }
    }
}
