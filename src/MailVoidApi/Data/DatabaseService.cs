using MySqlConnector;
using RoboDodd.OrmLite;
using MailVoidWeb;
using MailVoidWeb.Data.Models;
using MailVoidApi.Models;

namespace MailVoidApi.Data;

public interface IDatabaseService
{
    Task<MySqlConnection> GetConnectionAsync();
    Task InitializeAsync();
}

public class DatabaseService : IDatabaseService
{
    private readonly string _connectionString;
    private readonly ILogger<DatabaseService> _logger;

    public DatabaseService(IConfiguration configuration, ILogger<DatabaseService> logger)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found");
        _logger = logger;
    }

    public async Task<MySqlConnection> GetConnectionAsync()
    {
        var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();
        return connection;
    }

    public async Task InitializeAsync()
    {
        _logger.LogInformation("Initializing database tables...");

        using var db = await GetConnectionAsync();

        // Create tables in order of dependencies
        await db.CreateTableIfNotExistsAsync<User>();
        await db.CreateTableIfNotExistsAsync<Mail>();
        await db.CreateTableIfNotExistsAsync<Contact>();
        await db.CreateTableIfNotExistsAsync<MailGroup>();
        await db.CreateTableIfNotExistsAsync<MailGroupUser>();
        await db.CreateTableIfNotExistsAsync<RefreshToken>();
        await db.CreateTableIfNotExistsAsync<UserMailRead>();

        // Webhook tables
        await db.CreateTableIfNotExistsAsync<WebhookBucket>();
        await db.CreateTableIfNotExistsAsync<Webhook>();

        _logger.LogInformation("Database tables initialized successfully");
    }
}
