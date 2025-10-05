using Voice2Action.Domain;

namespace Voice2Action.Infrastructure.AI;

public sealed class DateTimeService : IDateTimeService
{
    public string GetCurrentDateTime()
    {
        var nowLocal = DateTime.Now;
        var nowUtc = DateTime.UtcNow;
        return $"LOCAL={nowLocal:yyyy-MM-ddTHH:mm:ssK};UTC={nowUtc:yyyy-MM-ddTHH:mm:ssZ}";
    }
}
