namespace Parorendeportalen.Api.Extensions;

public static class AntiforgeryExtensions
{
    public static IServiceCollection AddCsrfProtection(
        this IServiceCollection services,
        IWebHostEnvironment environment
    )
    {
        services.AddAntiforgery(options =>
        {
            options.HeaderName = "X-XSRF-TOKEN";

            // Same split as the session cookie: the __Host- prefix requires Secure,
            // which a dev run over plain http://localhost cannot set.
            options.Cookie.Name = environment.IsDevelopment() ? "pp.xsrf" : "__Host-pp.xsrf";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = environment.IsDevelopment()
                ? CookieSecurePolicy.SameAsRequest
                : CookieSecurePolicy.Always;
        });

        return services;
    }
}
