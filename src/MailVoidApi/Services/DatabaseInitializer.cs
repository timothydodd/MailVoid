using MailVoidApi.Services;
using MailVoidCommon;
using MailVoidCommon.Data.Models;
using ServiceStack.Data;
using ServiceStack.OrmLite;

namespace MailVoidApi.Data;

public class DatabaseInitializer
{
    private readonly IDbConnectionFactory _dbFactory;
    private readonly PasswordService _passwordService;

    public DatabaseInitializer(IDbConnectionFactory dbFactory, PasswordService passwordService)
    {
        _dbFactory = dbFactory;
        _passwordService = passwordService;
    }

    public void CreateTable()
    {
        using (var db = _dbFactory.OpenDbConnection())
        {
            db.CreateTableIfNotExists<Mail>();
            db.CreateTableIfNotExists<Contact>();
            if (db.CreateTableIfNotExists<User>())
            {
                var user = new User
                {
                    Id = Guid.NewGuid(),
                    UserName = "admin",
                    PasswordHash = "",
                    TimeStamp = DateTime.UtcNow
                };
                user.PasswordHash = _passwordService.HashPassword(user, "admin");
                db.Insert(user);
            }
        }
    }
}
