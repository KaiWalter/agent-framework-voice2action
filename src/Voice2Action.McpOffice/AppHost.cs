using System.ComponentModel;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Server;
using Voice2Action.Domain;
using Voice2Action.Infrastructure.AI;

// NOTE: Local functions previously caused MCP to surface mangled identifiers (s___Main___g__*). Using a
// static host class with real method names produces clean tool names.
internal static class OfficeToolHost
{
    internal static IReminderService? ReminderService;
    internal static IEmailService? EmailService;

    [Description("Set a reminder for the given task at the specified date and optional earlier reminder time.")]
    public static string SetReminder(
        [Description("Task to be reminded of.")] string task,
        [Description("Due date for the task.")] DateTime dueDate,
        [Description("Optional reminder date/time (before due date). ")] DateTime? reminderDate)
    {
        if (ReminderService is null) throw new InvalidOperationException("Reminder service not initialized yet.");
        return ReminderService.SetReminder(task, dueDate, reminderDate);
    }

    [Description("Send an email with the given subject and body to the user.")]
    public static string SendEmail(
        [Description("Email subject.")] string subject,
        [Description("Email body.")] string body)
    {
        if (EmailService is null) throw new InvalidOperationException("Email service not initialized yet.");
        return EmailService.SendEmail(subject, body);
    }
}

// Console app that exposes the OfficeAutomation agent (reminder + email) as an MCP HTTP server.
// Port override: MCP_HTTP_PORT_OFFICE

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Domain services required by OfficeAutomation tools
        builder.Services.AddSingleton<IReminderService, ReminderService>();
        builder.Services.AddSingleton<IEmailService, EmailService>();

        var tools = new[]
        {
            McpServerTool.Create(AIFunctionFactory.Create(new Func<string, DateTime, DateTime?, string>(OfficeToolHost.SetReminder))),
            McpServerTool.Create(AIFunctionFactory.Create(new Func<string, string, string>(OfficeToolHost.SendEmail)))
        };

        // Optional port override
        var portEnv = Environment.GetEnvironmentVariable("MCP_HTTP_PORT_OFFICE");
        if (int.TryParse(portEnv, out var port) && port > 0)
        {
            builder.WebHost.UseKestrel(o => o.ListenAnyIP(port));
        }

        builder.Services
            .AddMcpServer()
            .WithHttpTransport()
            .WithTools(tools);

        var app = builder.Build();
        var sp = app.Services;
        OfficeToolHost.ReminderService = sp.GetRequiredService<IReminderService>();
        OfficeToolHost.EmailService = sp.GetRequiredService<IEmailService>();

        app.MapGet("/healthz", () => new { status = "ok", tools = new[] { "SetReminder", "SendEmail" } });
        app.MapMcp();
        Console.WriteLine("OfficeAutomation MCP HTTP server started. Tools: SetReminder, SendEmail");
        app.Run();
    }
}