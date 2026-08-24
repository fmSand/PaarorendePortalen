using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Parorendeportalen.Api.Data;

namespace Parorendeportalen.Api.Tests.TestHelpers;

public sealed class SqliteInMemoryDbContextFactory : IDisposable
{
    private readonly SqliteConnection _connection;

    public SqliteInMemoryDbContextFactory()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        using var context = CreateContext();
        context.Database.EnsureCreated();
    }

    public AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;
        return new AppDbContext(options);
    }

    public void Dispose() => _connection.Dispose();
}
