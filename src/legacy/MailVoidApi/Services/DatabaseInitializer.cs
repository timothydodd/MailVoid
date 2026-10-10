using MailVoidApi.Data;
using MailVoidWeb.Data.Models;
using RoboDodd.OrmLite;

namespace MailVoidApi.Services;

public class DatabaseInitializer
{
    private readonly IDatabaseService _db;
    private readonly PasswordService _passwordService;
    private readonly IMailGroupService _mailGroupService;

    public DatabaseInitializer(IDatabaseService db, PasswordService passwordService, IMailGroupService mailGroupService)
    {
        _db = db;
        _passwordService = passwordService;
        _mailGroupService = mailGroupService;
    }

    public async Task SeedDefaultData()
    {
        using var db = await _db.GetConnectionAsync();

        // Check if admin user already exists
        var adminUser = await db.SingleAsync<User>(u => u.UserName == "admin");
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

            await db.InsertAsync(user);

            // Create a default private MailGroup for admin user
            await _mailGroupService.CreateUserPrivateMailGroup(user.Id, isDefault: true);
        }
        else if (adminUser.Role != Role.Admin)
        {
            // Update existing admin user to have Admin role
            adminUser.Role = Role.Admin;
            await db.UpdateAsync(adminUser);
        }

        // Ensure admin user has a private MailGroup (get the user again to ensure we have the right reference)
        var currentAdminUser = await db.SingleAsync<User>(u => u.UserName == "admin");
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
