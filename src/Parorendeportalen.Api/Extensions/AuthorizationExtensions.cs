using Microsoft.AspNetCore.Authorization;
using Parorendeportalen.Api.Authentication;

namespace Parorendeportalen.Api.Extensions;

public static class AuthorizationExtensions
{
    public static IServiceCollection AddKinshipAuthorization(this IServiceCollection services)
    {
        services.AddAuthorization(options =>
        {
            options.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build();
        });

        services.AddSingleton<IAuthorizationMiddlewareResultHandler, ApiChallengeResultHandler>();

        return services;
    }
}
