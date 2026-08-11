using Dapper;
using MailVoidApi.Models;
using MailVoidWeb;
using MailVoidWeb.Data.Models;
using Microsoft.Data.Sqlite;
using MySqlConnector;
using RoboDodd.OrmLite;

// Exports a MailVoid MySQL database to a SQLite database file.
// The SQLite schema is created from the same entity models the API uses,
// then data is copied table-by-table with primary keys preserved.

if (args.Length < 2 || args.Any(a => a is "-h" or "--help"))
{
    Console.WriteLine("Usage: dotnet run --project src/MailVoidDbExport -- <mysql-connection-string> <sqlite-file> [--overwrite]");
    Console.WriteLine();
    Console.WriteLine("Example:");
    Console.WriteLine("  dotnet run --project src/MailVoidDbExport -- \\");
    Console.WriteLine("    \"Server=localhost;Database=mailvoid;User=root;Password=secret\" mailvoid.db --overwrite");
    return 1;
}

var mysqlConnectionString = args[0];
var sqlitePath = args[1];
var overwrite = args.Contains("--overwrite");

if (File.Exists(sqlitePath))
{
    if (!overwrite)
    {
        Console.Error.WriteLine($"Target file '{sqlitePath}' already exists. Pass --overwrite to replace it.");
        return 1;
    }
    File.Delete(sqlitePath);
}

// Same conversion the API uses: GUIDs are stored as TEXT in SQLite
Dapper.SqlMapper.AddTypeHandler(new GuidTypeHandler());
Dapper.SqlMapper.AddTypeHandler(new NullableGuidTypeHandler());

await using var mysql = new MySqlConnection(mysqlConnectionString);
await mysql.OpenAsync();

await using var sqlite = new SqliteConnection($"Data Source={sqlitePath}");
await sqlite.OpenAsync();

Console.WriteLine("Creating SQLite schema...");

// Same order of dependencies as DatabaseService.InitializeAsync
await sqlite.CreateTableIfNotExistsAsync<User>(true);
await sqlite.CreateTableIfNotExistsAsync<Mail>(true);
await sqlite.CreateTableIfNotExistsAsync<Contact>(true);
await sqlite.CreateTableIfNotExistsAsync<MailGroup>(true);
await sqlite.CreateTableIfNotExistsAsync<MailGroupUser>(true);
await sqlite.CreateTableIfNotExistsAsync<RefreshToken>(true);
await sqlite.CreateTableIfNotExistsAsync<UserMailRead>(true);
await sqlite.CreateTableIfNotExistsAsync<WebhookBucket>(true);
await sqlite.CreateTableIfNotExistsAsync<Webhook>(true);

string[] tables = ["User", "Mail", "Contact", "MailGroup", "MailGroupUser", "RefreshToken", "UserMailRead", "WebhookBucket", "Webhook"];

await using var transaction = await sqlite.BeginTransactionAsync();

foreach (var table in tables)
{
    var existsInMySql = await mysql.ExecuteScalarAsync<int>(
        "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = DATABASE() AND table_name = @Table",
        new { Table = table }) > 0;
    if (!existsInMySql)
    {
        Console.WriteLine($"  {table}: not found in MySQL database, skipped");
        continue;
    }

    // Column list comes from the freshly created SQLite schema; only copy
    // columns that also exist in MySQL (tolerates older source schemas)
    var sqliteColumns = (await sqlite.QueryAsync<string>($"SELECT name FROM pragma_table_info('{table}')")).ToList();
    var mysqlColumns = (await mysql.QueryAsync<string>(
        "SELECT COLUMN_NAME FROM information_schema.columns WHERE table_schema = DATABASE() AND table_name = @Table",
        new { Table = table })).ToHashSet(StringComparer.OrdinalIgnoreCase);
    var columns = sqliteColumns.Where(c => mysqlColumns.Contains(c)).ToList();
    var skipped = sqliteColumns.Except(columns).ToList();
    if (skipped.Count > 0)
    {
        Console.WriteLine($"  {table}: columns missing in MySQL use defaults: {string.Join(", ", skipped)}");
    }

    var selectSql = $"SELECT {string.Join(", ", columns.Select(c => $"`{c}`"))} FROM `{table}`";
    var insertSql = $"INSERT INTO [{table}] ({string.Join(", ", columns.Select(c => $"[{c}]"))}) " +
                    $"VALUES ({string.Join(", ", columns.Select(c => $"@{c}"))})";

    var count = 0;
    // Unbuffered read so large Mail tables stream instead of loading into memory
    foreach (var row in await mysql.QueryAsync(new CommandDefinition(selectSql, flags: CommandFlags.None)))
    {
        var parameters = new DynamicParameters();
        foreach (var kvp in (IDictionary<string, object?>)row)
        {
            parameters.Add(kvp.Key, kvp.Value);
        }
        await sqlite.ExecuteAsync(insertSql, parameters, transaction);
        count++;
    }

    Console.WriteLine($"  {table}: {count} rows copied");
}

await transaction.CommitAsync();

Console.WriteLine($"Done. SQLite database written to '{sqlitePath}'.");
Console.WriteLine("Point the API at it with:");
Console.WriteLine($"  \"ConnectionStrings\": {{ \"DefaultConnection\": \"Data Source={sqlitePath}\" }}");
return 0;
