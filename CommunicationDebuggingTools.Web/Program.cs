using CommunicationDebuggingTools.Web.Components;
using CommunicationDebuggingTools.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

string engineHostBaseUrl = builder.Configuration["EngineHost:BaseUrl"] ?? "http://127.0.0.1:5101";
builder.Services.AddHttpClient<EngineHostApiClient>(http => {
    http.BaseAddress = new Uri(engineHostBaseUrl);
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment()) {
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found");
app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
