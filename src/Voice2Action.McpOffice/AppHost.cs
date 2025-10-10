using Microsoft.Extensions.AI;
using ModelContextProtocol.Server;
using Voice2Action.Domain;
using Voice2Action.Infrastructure.AI;
using Voice2Action.McpOffice;

// Console app that exposes the OfficeAutomation agent (reminder + email) as an MCP HTTP server.
// Minimal API (top-level statements)

var builder = WebApplication.CreateBuilder(args);

// Domain services required by OfficeAutomation tools
builder.Services.AddSingleton<IReminderService, ReminderService>();
builder.Services.AddSingleton<IEmailService, EmailService>();

var tools = new[]
{
    McpServerTool.Create(AIFunctionFactory.Create(new Func<string, DateTime, DateTime?, string>(OfficeToolHost.SetReminder))),
    McpServerTool.Create(AIFunctionFactory.Create(new Func<string, string, string>(OfficeToolHost.SendEmail))),
    McpServerTool.Create(AIFunctionFactory.Create(new Func<string, string, string>(OfficeToolHost.SendFallbackNotification)))
};

// NOTE: Port selection now handled by the root distributed AppHost. Remove any direct Kestrel port overrides here.

builder.Services
    .AddMcpServer()
    .WithHttpTransport()
    .WithTools(tools);

var app = builder.Build();
var sp = app.Services;
OfficeToolHost.ReminderService = sp.GetRequiredService<IReminderService>();
OfficeToolHost.EmailService = sp.GetRequiredService<IEmailService>();

app.MapGet("/healthz", () => new { status = "ok", tools = new[] { "SetReminder", "SendEmail", "SendFallbackNotification" } });
app.MapMcp();
Console.WriteLine("OfficeAutomation MCP HTTP server started. Tools: SetReminder, SendEmail, SendFallbackNotification");
app.Run();