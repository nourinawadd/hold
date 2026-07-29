using Hold.Components;

var builder = WebApplication.CreateBuilder(args);
// explanation: configures app with default settings, env variables, command line args and logging. 

// Add services to the container.
builder.Services.AddRazorComponents() // registers the services required to render Razor Components
    .AddInteractiveServerComponents(); // registers the services for server-side interactivity

var app = builder.Build(); // builds the app (everythin before it is resgistered, and everything after it is executed after the app is built)

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
