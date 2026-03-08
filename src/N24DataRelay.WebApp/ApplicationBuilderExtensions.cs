using N24DataRelay.WebApp.Hubs;

namespace N24DataRelay.WebApp;

/// <summary>Configures the web pipeline (routes, middleware) for the host.</summary>
public static class ApplicationBuilderExtensions
{
    /// <summary>Maps web app routes and middleware. Call after builder.Build() in the host.</summary>
    public static WebApplication UseWebApp(this WebApplication app)
    {
        // Swagger UI — available in all environments (admin-only intranet tool).
        // Access at /swagger
        app.UseSwagger();
        app.UseSwaggerUI(c =>
        {
            c.SwaggerEndpoint("/swagger/v1/swagger.json", "N24 Data Relay v1");
            c.DocumentTitle = "N24 Data Relay — API";
            // Persist the Bearer token across page reloads
            c.ConfigObject.PersistAuthorization = true;
        });

        app.UseAuthentication();
        app.UseAuthorization();
        app.MapRazorPages();
        app.MapControllers();
        app.MapHub<TransferHub>("/hubs/transfer");
        app.MapGet("/", () => Results.Redirect("/Index"));
        return app;
    }
}
