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
        [Description("Task/description for the reminder (alias: description).")] string description,
        [Description("Due date for the task (alias: date).")] DateTime date,
        [Description("Optional reminder date/time (before due date). ")] DateTime? reminderDate = null)
    {
        if (ReminderService is null) throw new InvalidOperationException("Reminder service not initialized yet.");
        if (string.IsNullOrWhiteSpace(description)) return ToolResultJson.Error("MissingArgument", "description required", "Reminder");
        try
        {
            var msg = ReminderService.SetReminder(description, date, reminderDate);
            return ToolResultJson.Ok("Reminder", new { task = description, dueDate = date.ToString("yyyy-MM-dd"), reminderDate = reminderDate?.ToString("yyyy-MM-dd"), message = msg });
        }
        catch (Exception ex)
        {
            return ToolResultJson.Error("ReminderFailed", ex.Message, "Reminder");
        }
    }

    [Description("Send an email with the given subject and body to the user.")]
    public static string SendEmail(
        [Description("Email subject.")] string subject,
        [Description("Email body.")] string body)
    {
        if (EmailService is null) throw new InvalidOperationException("Email service not initialized yet.");
        if (string.IsNullOrWhiteSpace(subject)) return ToolResultJson.Error("MissingArgument", "subject required", "Email");
        try
        {
            var msg = EmailService.SendEmail(subject, body);
            return ToolResultJson.Ok("Email", new { subject, bodyLength = body?.Length ?? 0, message = msg });
        }
        catch (Exception ex)
        {
            return ToolResultJson.Error("EmailFailed", ex.Message, "Email");
        }
    }

    [Description("Send a fallback notification (email) when no other actionable terminal intent was identified.")]
    public static string SendFallbackNotification(
        [Description("Short subject summarizing captured note.")] string subject = "Note captured",
        [Description("Full body content of the captured note or transcription.")] string body = "")
    {
        if (EmailService is null) throw new InvalidOperationException("Email service not initialized yet.");
        if (string.IsNullOrWhiteSpace(body)) return ToolResultJson.Error("MissingArgument", "body required for fallback notification", "FallbackNotification");
        try
        {
            var msg = EmailService.SendEmail(subject, body);
            return ToolResultJson.Ok("FallbackNotification", new { subject, bodyLength = body.Length, message = msg });
        }
        catch (Exception ex)
        {
            return ToolResultJson.Error("FallbackFailed", ex.Message, "FallbackNotification");
        }
    }
}
