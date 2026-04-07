using MailVoidApi.Models;
using MailVoidWeb;
using MailVoidWeb.Data.Models;
using MySqlConnector;
using RoboDodd.OrmLite;

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
        await db.CreateTableIfNotExistsAsync<User>(true);
        await db.CreateTableIfNotExistsAsync<Mail>(true);
        await db.CreateTableIfNotExistsAsync<Contact>(true);
        await db.CreateTableIfNotExistsAsync<MailGroup>(true);
        await db.CreateTableIfNotExistsAsync<MailGroupUser>(true);
        await db.CreateTableIfNotExistsAsync<RefreshToken>(true);
        await db.CreateTableIfNotExistsAsync<UserMailRead>(true);

        // Webhook tables
        await db.CreateTableIfNotExistsAsync<WebhookBucket>(true);
        await db.CreateTableIfNotExistsAsync<Webhook>(true);

        _logger.LogInformation("Database tables initialized successfully");
    }
}
