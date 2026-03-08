using Microsoft.Extensions.Options;
using N24DataRelay.Core.Models;
using N24DataRelay.WebApp.Hubs;

namespace N24DataRelay.WebApp;

/// <summary>Configures the web pipeline (routes, middleware) for the host.</summary>
public static class ApplicationBuilderExtensions
{
    /// <summary>Maps web app routes and middleware. Call after builder.Build() in the host.</summary>
    public static WebApplication UseWebApp(this WebApplication app)
    {
        // Global error handling — show developer details only in Development.
        if (app.Environment.IsDevelopment())
        {
            app.UseDeveloperExceptionPage();
        }
        else
        {
            app.UseExceptionHandler(errApp => errApp.Run(async ctx =>
            {
                ctx.Response.StatusCode  = StatusCodes.Status500InternalServerError;
                ctx.Response.ContentType = "application/json";
                await ctx.Response.WriteAsync("{\"error\":\"An unexpected error occurred.\"}");
            }));
        }

        // HTTPS redirect — only active when EnableHttps is configured.
        var kestrelConfig = app.Services
            .GetRequiredService<IOptionsMonitor<N24DataRelayConfiguration>>()
            .CurrentValue.WebPortal.Kestrel;
        if (kestrelConfig.EnableHttps)
        {
            app.UseHsts();
            app.UseHttpsRedirection();
        }

        app.UseStaticFiles();
        app.UseAuthentication();
        app.UseAuthorization();

        // Swagger — restricted to authenticated Admin users only.
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/swagger") &&
                !context.User.IsInRole("Admin"))
            {
                context.Response.Redirect("/Login");
                return;
            }
            await next(context);
        });
        app.UseSwagger();
        app.UseSwaggerUI(c =>
        {
            c.SwaggerEndpoint("/swagger/v1/swagger.json", "N24 Data Relay v1");
            c.DocumentTitle = "N24 Data Relay — API";
        });

        app.MapRazorPages();
        app.MapControllers();
        app.MapHub<TransferHub>("/hubs/transfer");
        app.MapGet("/", () => Results.Redirect("/Index"));
        return app;
    }
}
