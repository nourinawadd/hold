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

// Registered only when a key is actually configured, the same way the Claude extractor is.
// The Google handler validates its options on every request — not at startup — so wiring it
// up with an empty ClientId takes the whole site down with a 500, /health included. Absent
// credentials the app still runs: shared links work, the database is reachable, and only
// signing in is unavailable, which is a far better failure than an outage.
var google = builder.Configuration["Authentication:Google:ClientId"];
var googleReady = !string.IsNullOrWhiteSpace(google);

builder.Services.AddSingleton(new SignInAvailability(googleReady));

if (!googleReady)
{
    // Loud, because an app nobody can sign in to looks identical to a broken one.
    Console.WriteLine(
        "WARNING: Authentication:Google:ClientId is not configured. Sign-in is unavailable.");
}

// Cookie holds the session; Google is only reached when something issues a challenge. Both
// schemes are named explicitly rather than left to defaults, because the two roles differ:
// one proves who you are once, the other carries it on every request afterwards.
var authentication = builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;

        // Cookie, not Google — deliberately. An unauthenticated request to an [Authorize]
        // component issues a real HTTP challenge during static rendering, and challenging
        // Google there would throw the visitor at an account chooser before they had seen
        // this app at all. Pointing the default at the cookie handler sends them to its
        // LoginPath instead, which is our own sign-in page; only /account/login names the
        // Google scheme, and only after somebody has chosen to sign in.
        //
        // It also means a deployment with no Google credentials degrades to a page that
        // explains itself rather than to a scheme that does not exist.
        options.DefaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    })
    .AddCookie(options =>
    {
        options.LoginPath = "/sign-in";
        options.AccessDeniedPath = "/sign-in";

        // Spelled to match the [SupplyParameterFromQuery] on SignIn.razor. The handler's
        // default is "ReturnUrl", and relying on the two agreeing by case is the kind of
        // thing that silently drops people on the lists index instead of where they asked for.
        options.ReturnUrlParameter = "returnUrl";

        // Long enough that a wishlist does not ask you to sign in every visit, sliding so that
        // regular use never interrupts itself.
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
        // Same configuration shape as Anthropic:ApiKey below. As environment variables these
        // are AUTHENTICATION__GOOGLE__CLIENTID and __CLIENTSECRET — double underscores, which
        // is the spelling this repo has been bitten by before.
        options.ClientId = google!;
        options.ClientSecret = builder.Configuration["Authentication:Google:ClientSecret"] ?? string.Empty;

        // The account row is created here, before the cookie is written.
        options.Events.OnTicketReceived = GoogleSignIn.OnTicketReceivedAsync;
    });
}

builder.Services.AddAuthorization();

// Makes the authentication state available to components as a cascading value, which is what
// [Authorize] and AuthorizeRouteView read.
builder.Services.AddCascadingAuthenticationState();

builder.Services.AddScoped<CurrentUser>();
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

// Must come before anything that builds an absolute URL, which in practice means before
// authentication. The container is served plain HTTP behind a proxy that terminates TLS, so
// without this the app believes it is running on http:// and hands Google a redirect_uri of
// http://hold.nourin.me/signin-google — which Google refuses outright, since it allows plain
// HTTP only for localhost. The symptom is redirect_uri_mismatch and nothing in the app's own
// logs, because the request never gets that far.
//
// The Known* collections are cleared deliberately: they default to loopback only, and the
// proxy here is Azure's ingress at an address that is neither fixed nor knowable. Safe because
// nothing but that ingress can reach the container.
var forwarded = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost,
};

forwarded.KnownIPNetworks.Clear();
forwarded.KnownProxies.Clear();

app.UseForwardedHeaders(forwarded);

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


app.UseAuthentication();
app.UseAuthorization();

app.UseAntiforgery(); // means every form must carry a secret token that only pages served by my app can contain

// Sign-in and sign-out are HTTP endpoints, not component event handlers, and that is a
// constraint rather than a preference. An interactive Blazor Server circuit has no usable
// HttpContext and cannot write response headers, so an @onclick could never issue a redirect
// to Google or set a cookie. The sign-in page links here; the topbar posts here.
app.MapGet("/account/login", (string? returnUrl) =>
    !googleReady
        ? Results.Text("Sign-in is not configured on this deployment.", "text/plain", statusCode: 503)
        : Results.Challenge(
        new AuthenticationProperties
        {
            // Local paths only. Echoing an arbitrary returnUrl back into a redirect is how an
            // open redirect gets built, and this one would carry a freshly signed-in session.
            RedirectUri = returnUrl is not null && Uri.IsWellFormedUriString(returnUrl, UriKind.Relative)
                ? returnUrl
                : "/",
        },
        [GoogleDefaults.AuthenticationScheme]));

// POST, so the browser cannot be talked into signing someone out by loading an image, and so
// antiforgery applies.
app.MapPost("/account/logout", async (HttpContext http) =>
{
    await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

    return Results.Redirect("/sign-in");
});

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
