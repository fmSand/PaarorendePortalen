using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;

namespace Parorendeportalen.Api.Tests.TestHelpers;

/// <summary>
/// The full application over a throwaway Postgres database. Demo environment authenticates
/// every request as a seeded next-of-kin, so writes are reachable without BankID.
/// </summary>
/// <remarks>
/// Boots Program.cs, so this also proves migrations apply. Background workers are off,
/// or a sync tick mid-test would make assertions timing-dependent. Nothing from Idura and
/// no pepper is supplied, which is all a local Demo run has: if Demo starts demanding
/// either again, every pipeline test fails the way the local run does.
/// </remarks>
internal sealed class PortalApplicationFactory(string connectionString)
    : WebApplicationFactory<Program>
{
    // Program.cs reads these eagerly, before ConfigureAppConfiguration can reach them, but
    // CreateBuilder does read env vars. Set around CreateHost, cleared after: tests run
    // serially in one xUnit collection.
    private static readonly string[] Keys =
    [
        "ConnectionStrings__Default",
        "VisitSync__Enabled",
        "Notifications__Enabled",
    ];

    protected override IHost CreateHost(IHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseEnvironment("Demo");

        string[] values = [connectionString, "false", "false"];

        for (var index = 0; index < Keys.Length; index++)
        {
            Environment.SetEnvironmentVariable(Keys[index], values[index]);
        }

        try
        {
            return base.CreateHost(builder);
        }
        finally
        {
            foreach (var key in Keys)
            {
                Environment.SetEnvironmentVariable(key, null);
            }
        }
    }

    public HttpClient CreateSecureClient() =>
        CreateClient(
            new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") }
        );
}
