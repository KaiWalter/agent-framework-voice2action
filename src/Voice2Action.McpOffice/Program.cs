using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Voice2Action.Domain;
using Voice2Action.Infrastructure.AI;
using Voice2Action.Application;

// Console app that exposes the OfficeAutomation agent (reminder + email) as an MCP HTTP server.
// Port override: MCP_HTTP_PORT_OFFICE

var builder = Host.CreateDefaultBuilder(args)
    .ConfigureAppConfiguration(cfg => cfg.AddEnvironmentVariables())
    .ConfigureServices(services =>
    {
        services.AddSingleton<IReminderService, ReminderService>();
        services.AddSingleton<IEmailService, EmailService>();
    });

var host = builder.Build();
var sp = host.Services;
var reminder = sp.GetRequiredService<IReminderService>();
var email = sp.GetRequiredService<IEmailService>();
Console.WriteLine("Starting OfficeAutomation MCP HTTP server (reminder + email). Press Ctrl+C to exit.");
await OfficeAutomationMcpServer.RunAsync(reminder, email, CancellationToken.None);
