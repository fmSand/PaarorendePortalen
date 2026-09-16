using Microsoft.EntityFrameworkCore;
using Npgsql;
using Parorendeportalen.Api.Data;

namespace Parorendeportalen.Api.Tests.TestHelpers;

/// <summary>
/// A throwaway database on the shared Postgres container, created per test and
/// dropped afterwards. Schema is built from the model via <c>EnsureCreated</c>
/// </summary>
public sealed class PostgresTestDatabase : IAsyncDisposable
{
    private readonly string _adminConnectionString;
    private readonly string _connectionString;
    private readonly string _databaseName;

    private PostgresTestDatabase(
        string adminConnectionString,
        string connectionString,
        string databaseName
    )
    {
        _adminConnectionString = adminConnectionString;
        _connectionString = connectionString;
        _databaseName = databaseName;
    }

    public static Task<PostgresTestDatabase> CreateAsync(string baseConnectionString) =>
        CreateAsync(baseConnectionString, withSchema: true);

    /// <summary>
    /// Empty database, no schema. For the pipeline test, which migrates on startup and would
    /// fail on the first CREATE TABLE against a schema already built from the model.
    /// </summary>
    public static Task<PostgresTestDatabase> CreateWithoutSchemaAsync(
        string baseConnectionString
    ) => CreateAsync(baseConnectionString, withSchema: false);

    private static async Task<PostgresTestDatabase> CreateAsync(
        string baseConnectionString,
        bool withSchema
    )
    {
        var databaseName = "test_" + Guid.NewGuid().ToString("n");
        var builder = new NpgsqlConnectionStringBuilder(baseConnectionString);
        var adminConnectionString = builder.ConnectionString;
        builder.Database = databaseName;
        var connectionString = builder.ConnectionString;

        await using (var admin = new NpgsqlConnection(adminConnectionString))
        {
            await admin.OpenAsync();
            await using var create = admin.CreateCommand();
            create.CommandText = $"CREATE DATABASE \"{databaseName}\"";
            await create.ExecuteNonQueryAsync();
        }

        var database = new PostgresTestDatabase(
            adminConnectionString,
            connectionString,
            databaseName
        );
        if (withSchema)
        {
            await using var context = database.CreateContext();
            await context.Database.EnsureCreatedAsync();
        }

        return database;
    }

    public string ConnectionString => _connectionString;

    public AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_connectionString).Options);

    public async ValueTask DisposeAsync()
    {
        NpgsqlConnection.ClearPool(new NpgsqlConnection(_connectionString));

        await using var admin = new NpgsqlConnection(_adminConnectionString);
        await admin.OpenAsync();
        await using var drop = admin.CreateCommand();
        // WITH (FORCE) terminates any lingering connections so the drop can't hang.
        drop.CommandText = $"DROP DATABASE IF EXISTS \"{_databaseName}\" WITH (FORCE)";
        await drop.ExecuteNonQueryAsync();
    }
}
