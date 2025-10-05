using Microsoft.Extensions.DependencyInjection;
using Voice2Action.Domain;

namespace Voice2Action.Infrastructure.AI;

public static class OrchestrationServiceCollectionExtensions
{
    public static IServiceCollection AddVoiceCommandOrchestrator(
        this IServiceCollection services,
        ITextAgent coordinator,
        IEnumerable<ITextAgent> workers)
    {
        var workerList = workers.ToList();
        services.AddSingleton<IVoiceCommandOrchestrator>(sp => new VoiceCommandOrchestrator(coordinator, workerList));
        return services;
    }
}
