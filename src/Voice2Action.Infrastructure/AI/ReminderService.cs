using Voice2Action.Domain;

namespace Voice2Action.Infrastructure.AI;

public sealed class ReminderService : IReminderService
{
    public string SetReminder(string task, DateTime dueDate, DateTime? reminderDate)
    {
        var baseMsg = $"Reminder set for task '{task}' due at {dueDate}.";
        return reminderDate.HasValue
            ? baseMsg + $" Reminder will trigger at {reminderDate.Value}."
            : baseMsg;
    }
}
