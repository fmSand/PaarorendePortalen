using Testcontainers.PostgreSql;

namespace Parorendeportalen.Api.Tests.TestHelpers;

/// <summary>
/// One Postgres container, shared across every repository test in the collection.
/// </summary>
/// <remarks>
/// Repositories rely on Postgres-specific translation
/// (e.g. EfVisitRepository's SQL-side DateTimeOffset filtering
/// and ORDER BY), so tests must run against the engine.
/// Per-test isolation comes from
/// <see cref="PostgresTestDatabase"/> creating a fresh database on this
/// container for each test.
/// </summary>
public sealed class PostgresContainerFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresContainerFixture>
{
    public const string Name = "Postgres";
}
