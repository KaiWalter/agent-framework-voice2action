using Microsoft.Extensions.DependencyInjection;
using Voice2Action.Domain;
using Voice2Action.Application;
using Azure.AI.OpenAI;

namespace Voice2Action.Infrastructure.AI;

public static class OrchestrationServiceCollectionExtensions
{
    public sealed class AgentSetOptions
    {
        public string CoordinatorName { get; set; } = "Planner";
        public string PromptDirectory { get; set; } = string.Empty; // absolute path required
        public string DeploymentName { get; set; } = string.Empty; // chat deployment
    }

    public static IServiceCollection AddAgentSetProvider(this IServiceCollection services, Action<AgentSetOptions> configure)
    {
        var opts = new AgentSetOptions();
        configure(opts);
        services.AddSingleton<IAgentSetProvider>(sp => new DefaultAgentSetProvider(
            sp.GetRequiredService<AzureOpenAIClient>(),
            sp.GetRequiredService<ITranscriptionService>(),
            sp.GetRequiredService<IDateTimeService>(),
            sp.GetRequiredService<IReminderService>(),
            sp.GetRequiredService<IEmailService>(),
            opts.DeploymentName,
            opts.PromptDirectory,
            opts.CoordinatorName));
        return services;
    }

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
