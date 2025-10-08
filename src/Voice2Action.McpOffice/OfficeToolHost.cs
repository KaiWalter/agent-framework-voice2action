using System.ComponentModel;
using Voice2Action.Domain;
using Voice2Action.Infrastructure.AI;

namespace Voice2Action.McpOffice;

// Hosts the office automation MCP tools (reminder + email)
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
