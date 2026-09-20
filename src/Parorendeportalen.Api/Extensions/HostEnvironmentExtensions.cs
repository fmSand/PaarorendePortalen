namespace Parorendeportalen.Api.Extensions;

public static class HostEnvironmentExtensions
{
    public static bool IsDemo(this IHostEnvironment environment) =>
        environment.IsEnvironment("Demo");
}
