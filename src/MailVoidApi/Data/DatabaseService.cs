using System.Data.Common;
using Dapper;
using MailVoidApi.Models;
using MailVoidWeb;
using MailVoidWeb.Data.Models;
using Microsoft.Data.Sqlite;
using MySqlConnector;
using RoboDodd.OrmLite;

namespace MailVoidApi.Data;

public interface IDatabaseService
{
    DatabaseProvider Provider { get; }
    Task<DbConnection> GetConnectionAsync();
    Task InitializeAsync();
}

public class DatabaseService : IDatabaseService
{
    private readonly string _connectionString;
    private readonly ILogger<DatabaseService> _logger;

    public DatabaseProvider Provider { get; }

    public DatabaseService(IConfiguration configuration, ILogger<DatabaseService> logger)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found");
        _logger = logger;
        Provider = DetectProvider(configuration);

        if (Provider == DatabaseProvider.SQLite)
        {
            // GUIDs are stored as TEXT in SQLite; Dapper needs handlers to convert them
            Dapper.SqlMapper.AddTypeHandler(new GuidTypeHandler());
            Dapper.SqlMapper.AddTypeHandler(new NullableGuidTypeHandler());
        }
    }

    /// <summary>
    /// Resolves the database provider from the optional "DatabaseProvider" setting
    /// ("Sqlite" or "MySql"), falling back to connection-string detection.
    /// </summary>
    public static DatabaseProvider DetectProvider(IConfiguration configuration)
    {
        var configured = configuration["DatabaseProvider"];
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured.Trim().ToLowerInvariant() switch
            {
                "sqlite" => DatabaseProvider.SQLite,
                "mysql" => DatabaseProvider.MySql,
                _ => throw new InvalidOperationException(
                    $"Unsupported DatabaseProvider '{configured}'. Supported values: Sqlite, MySql")
            };
        }

        var connectionString = configuration.GetConnectionString("DefaultConnection") ?? string.Empty;
        return connectionString.Contains("Data Source", StringComparison.OrdinalIgnoreCase)
            ? DatabaseProvider.SQLite
            : DatabaseProvider.MySql;
    }

    public async Task<DbConnection> GetConnectionAsync()
    {
        if (Provider == DatabaseProvider.SQLite)
        {
            var sqlite = new SqliteConnection(_connectionString);
            await sqlite.OpenAsync();
            // SQLite disables foreign key enforcement per connection by default
            await sqlite.ExecuteAsync("PRAGMA foreign_keys = ON;");
            return sqlite;
        }

        var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();
        return connection;
    }

    public async Task InitializeAsync()
    {
        _logger.LogInformation("Initializing database tables using {Provider}...", Provider);

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
