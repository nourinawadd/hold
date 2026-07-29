using Hold.Components;
using Hold.Data;
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
// SQLite has no decimal type, so EF stores decimal as TEXT, where comparison and ordering
// are lexicographic. That is accepted deliberately: money is always materialised and
// aggregated in C#, never in SQL. See ListService.
builder.Services.AddDbContextFactory<HoldDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Hold")));

builder.Services.AddScoped<ListService>();
builder.Services.AddScoped<SettingsService>();

var app = builder.Build(); // builds the app (everythin before it is resgistered, and everything after it is executed after the app is built)

// Development only. Production migrations are applied at deploy time (phase 8).
if (app.Environment.IsDevelopment())
{
    await using var scope = app.Services.CreateAsyncScope();
    var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<HoldDbContext>>();
    await using var db = await factory.CreateDbContextAsync();

    await db.Database.MigrateAsync();
    await DevDataSeeder.SeedAsync(db, scope.ServiceProvider.GetRequiredService<TimeProvider>());
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    // this shows a generic error page instead of a stack trace. when in dev you want the stack trace
    app.UseHsts(); // stops browser from connecting to this app over HTTP for 3o days
}

app.UseHttpsRedirection();


app.UseAntiforgery(); // means every form must carry a secret token that only pages served by my app can contain

app.MapStaticAssets(); // maps static assets so they can be served
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run(); // starts the app and blocks anything after it from running until the app is shutdown.
