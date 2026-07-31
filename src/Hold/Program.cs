using Hold.Components;
using Hold.Data;
using Hold.Scraping;
using Hold.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
// explanation: configures app with default settings, env variables, command line args and logging.

builder.Services.AddRazorComponents() // registers the services required to render Razor Components
    .AddInteractiveServerComponents(); // registers the services for server-side interactivity

builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddDbContextFactory<HoldDbContext>(options =>
    options.UseNpgsql(
        PostgresConnection.Resolve(builder.Configuration),
        postgres => postgres.EnableRetryOnFailure(
            maxRetryCount: 3,
            maxRetryDelay: TimeSpan.FromSeconds(5),
            errorCodesToAdd: null)));

var google = builder.Configuration["Authentication:Google:ClientId"];
var googleReady = !string.IsNullOrWhiteSpace(google);

builder.Services.AddSingleton(new SignInAvailability(googleReady));

if (!googleReady)
{
    Console.WriteLine(
        "WARNING: Authentication:Google:ClientId is not configured. Sign-in is unavailable.");
}

var authentication = builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    })
    .AddCookie(options =>
    {
        options.LoginPath = "/sign-in";
        options.AccessDeniedPath = "/sign-in";

        options.ReturnUrlParameter = "returnUrl";

        options.ExpireTimeSpan = TimeSpan.FromDays(30);
        options.SlidingExpiration = true;

        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    });

if (googleReady)
{
    authentication.AddGoogle(options =>
    {
        options.ClientId = google!;
        options.ClientSecret = builder.Configuration["Authentication:Google:ClientSecret"] ?? string.Empty;

        options.Events.OnTicketReceived = GoogleSignIn.OnTicketReceivedAsync;
    });
}

builder.Services.AddAuthorization();

builder.Services.AddCascadingAuthenticationState();

builder.Services.AddScoped<CurrentUser>();
builder.Services.AddScoped<ListService>();
builder.Services.AddScoped<ItemService>();
builder.Services.AddScoped<SettingsService>();
builder.Services.AddScoped<StatsService>();

builder.Services.AddSingleton<IProductExtractor>(services =>
    new ClaudeProductExtractor(
        builder.Configuration["Anthropic:ApiKey"],
        services.GetRequiredService<ILogger<ClaudeProductExtractor>>()));

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

await using (var scope = app.Services.CreateAsyncScope())
{
    var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<HoldDbContext>>();
    await using var db = await factory.CreateDbContextAsync();

    await db.Database.MigrateAsync();

    var clock = scope.ServiceProvider.GetRequiredService<TimeProvider>();

    await DemoSeeder.SeedAsync(db, clock);

    if (app.Environment.IsDevelopment())
    {
        await DevDataSeeder.SeedAsync(db, clock);
    }
}

var forwarded = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost,
};

forwarded.KnownIPNetworks.Clear();
forwarded.KnownProxies.Clear();

app.UseForwardedHeaders(forwarded);

if (!app.Environment.IsDevelopment())
{
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

if (!string.IsNullOrEmpty(builder.Configuration["ASPNETCORE_HTTPS_PORTS"])
    || !string.IsNullOrEmpty(builder.Configuration["HTTPS_PORTS"]))
{
    if (!app.Environment.IsDevelopment())
    {
        app.UseHsts();
    }

    app.UseHttpsRedirection();
}


app.UseAuthentication();
app.UseAuthorization();

app.UseAntiforgery(); // means every form must carry a secret token that only pages served by my app can contain

app.MapGet("/account/login", (string? returnUrl) =>
    !googleReady
        ? Results.Text("Sign-in is not configured on this deployment.", "text/plain", statusCode: 503)
        : Results.Challenge(
        new AuthenticationProperties
        {
            RedirectUri = returnUrl is not null && Uri.IsWellFormedUriString(returnUrl, UriKind.Relative)
                ? returnUrl
                : "/",
        },
        [GoogleDefaults.AuthenticationScheme]));

app.MapPost("/account/logout", async (HttpContext http) =>
{
    await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

    return Results.Redirect("/sign-in");
});

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
