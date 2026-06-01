using ModelContextProtocol.AspNetCore;
using SmartDesk.McpServer.Ticket.Services;
using SmartDesk.McpServer.Ticket.Tools;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMcpServer()
    .WithHttpTransport(options =>
    {
        options.Stateless = true;
    })
    .WithTools<TicketTools>();

builder.Services.AddSingleton<ITicketStore, InMemoryTicketStore>();

builder.Services.AddLogging(l =>
{
    l.AddConsole();
    l.SetMinimumLevel(LogLevel.Information);
});

var app = builder.Build();

app.MapMcp("/mcp");

app.MapGet("/health", () => Results.Ok(new
{
    status = "healthy",
    server = "TicketMcpServer",
    version = "1.0.0",
    timestamp = DateTime.UtcNow
}));

app.MapGet("/debug/tickets", async (ITicketStore store) =>
{
    var tickets = await store.ListAsync();
    return Results.Ok(tickets);
});

app.Run();
