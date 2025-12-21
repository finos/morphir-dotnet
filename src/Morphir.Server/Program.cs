using System.Text.Json.Serialization;
using Microsoft.AspNetCore.SignalR;
using Foundatio.Mediator;
using Morphir.Server.Hubs;
using Morphir.Tooling.Infrastructure.JsonSchema;

var builder = WebApplication.CreateBuilder(args);

// Add services
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddSignalR();
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// Add Foundatio.Mediator
builder.Services.AddMediator();

// Add infrastructure services from Morphir.Tooling
builder.Services.AddSingleton<SchemaLoader>();
builder.Services.AddSingleton<SchemaValidator>();

// Add Razor Components with SSR and WASM support (for hosting Morphir.Web)
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Add health checks
builder.Services.AddHealthChecks();

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();

// Configure the request pipeline
app.UseStaticFiles();
app.UseRouting();
app.UseAntiforgery();

// Health check endpoint
app.MapGet("/health", () => Results.Ok(new { Status = "Healthy", Timestamp = DateTime.UtcNow }))
   .WithName("HealthCheck")
   .WithTags("Health");

// Server info endpoint
app.MapGet("/api/server/info", () =>
{
    var assembly = System.Reflection.Assembly.GetExecutingAssembly();
    var version = assembly.GetName().Version?.ToString() ?? "unknown";

    return Results.Ok(new
    {
        Name = "Morphir Server",
        Version = version,
        Framework = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
        Environment = app.Environment.EnvironmentName,
        Timestamp = DateTime.UtcNow,
        Features = new
        {
            SignalR = true,
            MinimalApi = true,
            Mediator = "Foundatio.Mediator"
        }
    });
})
.WithName("ServerInfo")
.WithTags("Server");

// SignalR hub
app.MapHub<MorphirHub>("/morphirHub");

// Map Blazor app (fallback for all other routes)
// F#/C# interop: Morphir.Web.App is a public F# type accessible from C#
app.MapRazorComponents<Morphir.Web.App>()
    .AddInteractiveServerRenderMode();

app.Run();

// Make Program class accessible to tests (required for WebApplicationFactory<Program>)
namespace Morphir.Server
{
    public partial class Program { }
}
