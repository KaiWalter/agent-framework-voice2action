using System.ComponentModel;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Hosting;
using ModelContextProtocol.Server;
using Voice2Action.Domain;

namespace Voice2Action.Application;

/// <summary>
/// Exposes the OfficeAutomation agent's reminder + email capabilities as an HTTP MCP server.
/// </summary>
public static class OfficeAutomationMcpServer
{
    public static async Task RunAsync(IReminderService reminderService, IEmailService emailService, CancellationToken cancellationToken = default)
    {
        if (reminderService is null) throw new ArgumentNullException(nameof(reminderService));
        if (emailService is null) throw new ArgumentNullException(nameof(emailService));

        [Description("Set a reminder for the given task at the specified date and optional earlier reminder time.")]
        static string SetReminderInner(IReminderService svc,
            [Description("Task to be reminded of.")] string task,
            [Description("Due date for the task.")] DateTime dueDate,
            [Description("Optional reminder date/time (before due date). ")] DateTime? reminderDate)
        {
            return svc.SetReminder(task, dueDate, reminderDate);
        }

        [Description("Send an email with the given subject and body to the user.")]
        static string SendEmailInner(IEmailService svc,
            [Description("Email subject.")] string subject,
            [Description("Email body.")] string body)
        {
            return svc.SendEmail(subject, body);
        }

        string SetReminder(string task, DateTime dueDate, DateTime? reminderDate) => SetReminderInner(reminderService, task, dueDate, reminderDate);
        string SendEmail(string subject, string body) => SendEmailInner(emailService, subject, body);

        var tools = new[]
        {
            McpServerTool.Create(AIFunctionFactory.Create(new Func<string, DateTime, DateTime?, string>(SetReminder))),
            McpServerTool.Create(AIFunctionFactory.Create(new Func<string, string, string>(SendEmail)))
        };

        var webBuilder = WebApplication.CreateBuilder();
        var portEnv = Environment.GetEnvironmentVariable("MCP_HTTP_PORT_OFFICE");
        if (int.TryParse(portEnv, out var port) && port > 0)
        {
            webBuilder.WebHost.ConfigureKestrel(o => o.ListenAnyIP(port));
        }

        webBuilder.Services
            .AddMcpServer()
            .WithHttpTransport()
            .WithTools(tools);

        var app = webBuilder.Build();
    app.MapGet("/healthz", () => new { status = "ok", tools = new[] { "SetReminder", "SendEmail" } });
        app.MapMcp();
        Console.WriteLine("OfficeAutomation MCP HTTP server started. Tools: SetReminder, SendEmail");
        await app.RunAsync(cancellationToken).ConfigureAwait(false);
    }
}
