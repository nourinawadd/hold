using Hold.Components;
using Hold.Data;
using Hold.Scraping;
using Hold.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
// explanation: configures app with default settings, env variables, command line args and logging.

// Add services to the container.
builder.Services.AddRazorComponents() // registers the services required to render Razor Components
    .AddInteractiveServerComponents(); // registers the services for server-side interactivity

// Injected wherever "now" is needed, so wait tracking can be tested without waiting.
builder.Services.AddSingleton(TimeProvider.System);

// A factory, not a scoped context: a Blazor Server circuit outlives a request by hours,
// and a context living that long accumulates tracked entities and breaks when two
// renders overlap. Services create a short-lived context per operation instead.
builder.Services.AddDbContextFactory<HoldDbContext>(options =>
    options.UseNpgsql(
        PostgresConnection.Resolve(builder.Configuration),
        // A managed Postgres on a free tier sleeps when idle, so the first query after a quiet
        // spell can lose the race to wake it. Retrying is only safe because nothing here opens
        // an explicit transaction — EF's execution strategy refuses to manage one it did not
        // start.
        postgres => postgres.EnableRetryOnFailure(
            maxRetryCount: 3,
            maxRetryDelay: TimeSpan.FromSeconds(5),
            errorCodesToAdd: null)));

builder.Services.AddScoped<ListService>();
builder.Services.AddScoped<ItemService>();
builder.Services.AddScoped<SettingsService>();

// The scraper's last-resort strategy. Registered unconditionally but inert without a key —
// no key means Enabled is false, the parser is left out of the chain, and nothing is
// billed. Set it in user secrets or the ANTHROPIC__APIKEY environment variable; never
// commit it.
builder.Services.AddSingleton<IProductExtractor>(services =>
    new ClaudeProductExtractor(
        builder.Configuration["Anthropic:ApiKey"],
        services.GetRequiredService<ILogger<ClaudeProductExtractor>>()));

// A typed client, never `new HttpClient()`: that leaks sockets and pins stale DNS.
// Shops serve different markup to something that does not look like a browser, so the
// headers matter as much as the timeout.
builder.Services.AddHttpClient<ProductScraper>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(12);
    client.DefaultRequestHeaders.UserAgent.ParseAdd(
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36");
    client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.9");
    client.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml,application/json;q=0.9,*/*;q=0.8");
})
.ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
{
    AllowAutoRedirect = true,
    MaxAutomaticRedirections = 5,
});

var app = builder.Build(); // builds the app (everythin before it is resgistered, and everything after it is executed after the app is built)

// Migrations run in every environment, so an empty database becomes a working one with no
// separate deploy step. The usual objection is several instances racing to migrate at once.
// Postgres would happily let them try — unlike SQLite, it does not serialise writers for us —
// so what rules it out here is the deployment: Render's free tier runs exactly one instance.
// Scaling past one means moving this to a release command that runs before the app starts.
await using (var scope = app.Services.CreateAsyncScope())
{
    var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<HoldDbContext>>();
    await using var db = await factory.CreateDbContextAsync();

    await db.Database.MigrateAsync();

    // Seeding is Development-only. Production must never invent data.
    if (app.Environment.IsDevelopment())
    {
        await DevDataSeeder.SeedAsync(db, scope.ServiceProvider.GetRequiredService<TimeProvider>());
    }
}

if (!app.Environment.IsDevelopment())
{
    // A plain sentence, written inline rather than routed: an /Error page would be a route to
    // keep in sync, and the previous handler pointed at one that never existed.
    app.UseExceptionHandler(errors => errors.Run(async context =>
    {
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "text/html; charset=utf-8";

        await context.Response.WriteAsync(
            """
            <!doctype html><meta charset="utf-8"><title>Hold</title>
            <body style="font-family:ui-monospace,monospace;background:#F2F0EA;color:#16161A;padding:3rem">
              <p>Something went wrong on our side. Nothing you saved has been lost.</p>
              <p><a href="/" style="color:#16161A">Back to your lists</a></p>
            </body>
            """);
    }));
}

// Both are hostile to a container that serves plain HTTP behind something else terminating
// TLS: the redirect has nowhere to send anyone, and HSTS would tell the browser to force
// HTTPS against an origin that has none. Only enabled when an HTTPS port really is configured.
if (!string.IsNullOrEmpty(builder.Configuration["ASPNETCORE_HTTPS_PORTS"])
    || !string.IsNullOrEmpty(builder.Configuration["HTTPS_PORTS"]))
{
    if (!app.Environment.IsDevelopment())
    {
        app.UseHsts();
    }

    app.UseHttpsRedirection();
}


app.UseAntiforgery(); // means every form must carry a secret token that only pages served by my app can contain

// Opens the database rather than just answering. An app that is running but cannot reach its
// database is not healthy, and with the database now a separate service across a network that
// is the failure most worth catching.
app.MapGet("/health", async (IDbContextFactory<HoldDbContext> factory) =>
{
    await using var db = await factory.CreateDbContextAsync();
    await db.WishLists.CountAsync();

    return Results.Text("ok");
});

app.MapStaticAssets(); // maps static assets so they can be served
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run(); // starts the app and blocks anything after it from running until the app is shutdown.
