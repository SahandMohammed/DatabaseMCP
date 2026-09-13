using DreamItERP.DatabaseMcp.Services;
using DreamItERP.DatabaseMcp.Tools;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Server;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<DatabaseInspector>();
builder.Services.AddSingleton<DatabaseExecutor>();
builder.Services.AddSingleton<InventoryDashboardVerifier>();

builder.Services
    .AddMcpServer()
    .WithHttpTransport(options =>
    {
        options.SessionMode = HttpServerSessionMode.Stateless;
    })
    .WithTools<DatabaseTools>();

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new
{
    status = "ok",
    service = "DreamItERP Database MCP"
}));

app.MapMcp("/mcp");

app.Run();