using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using System.ComponentModel; // For Description attributes
using Voice2Action.Domain;

namespace Voice2Action.Infrastructure.AI;

internal static class OfficeAutomationAgentFactory
{
    public static ITextAgent Create(IChatClient chatClient, string promptPath, IReminderService reminderService, IEmailService emailService)
    {
        var instructions = File.ReadAllText(Path.Combine(promptPath, "office-automation.md"));
        // Local functions with Description attributes (migrated from former Program.cs tool methods)
        [Description("Set a reminder for the given task at the specified date and optional earlier reminder time.")]
        string SetReminder(
            [Description("Task to be reminded of.")] string task,
            [Description("Due date for the task.")] DateTime dueDate,
            [Description("Optional reminder date/time (before due date).")] DateTime? reminderDate)
        {
            return reminderService.SetReminder(task, dueDate, reminderDate);
        }

        [Description("Send an email with the given subject and body to the user.")]
        string SendEmail(
            [Description("Email subject.")] string subject,
            [Description("Email body.")] string body)
        {
            return emailService.SendEmail(subject, body);
        }

        var options = new ChatClientAgentOptions(instructions)
        {
            ChatOptions = new ChatOptions
            {
                Tools =
                [
                    AIFunctionFactory.Create(new Func<string, DateTime, DateTime?, string>(SetReminder)),
                    AIFunctionFactory.Create(new Func<string, string, string>(SendEmail))
                ]
            }
        };
        var core = new ChatClientAgent(chatClient, options);
        return new BasicTextAgent("OfficeAutomation", new[] { "SetReminder(task, dueDate, reminderDate?)", "SendEmail(subject, body)" }, core);
    }
}
