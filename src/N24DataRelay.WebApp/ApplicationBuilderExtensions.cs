namespace N24DataRelay.WebApp;

/// <summary>Configures the web pipeline (routes, middleware) for the host.</summary>
public static class ApplicationBuilderExtensions
{
    /// <summary>Maps web app routes and middleware. Call after builder.Build() in the host.</summary>
    public static WebApplication UseWebApp(this WebApplication app)
    {
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapRazorPages();
        app.MapGet("/", () => Results.Redirect("/Index"));
        return app;
    }
}
