# Agent Framework Voice 2 Action agentic flow

This repository explores personal productivity flows using the [Microsoft Agent Framework](https://aka.ms/AgentFramework), implementing voice-to-action workflows that automatically process voice recordings and execute appropriate actions.

Original flow as implemented here - see [my post](https://dev.to/kaiwalter/dipping-into-dapr-agentic-workflows-fbi) for more background:

1. polling on OneDrive, downloading and transcribing the voice recording runs in a deterministic workflow
2. transcript is then handed to LLM-orchestrated agents which have instructions to figure out what to do with the transcription
3. agents to make use of tools (probably MCP servers in the future) to interact with the outside world

## Current Architecture (Clean Architecture Adaptation)

The solution has been refactored into a layered Clean Architecture structure:

```
src/
	Voice2Action.Domain/            # Core models and port interfaces (no external deps)
	Voice2Action.Application/       # Use cases / orchestration (depends on Domain only)
	Voice2Action.Infrastructure/    # AI + messaging adapters (depends on Application + Domain)
	Voice2Action.Console/           # Composition root / entry point
```

Key abstractions:
- ISpamDetectionService: classify email content
- IEmailDraftService: draft a professional response
- IEmailSender: deliver a response (currently console)
- ISpamDispositionService: handle spam outcomes

Flow implemented in `ProcessIncomingEmail` (Application). Infrastructure provides Azure OpenAI agent-backed implementations. Console sets up DI and invokes the use case.

Benefits:
- Swap AI provider without touching business logic
- Unit-test use case with in-memory fakes
- Future channels (API, queue trigger, voice ingestion) only replace the outer layer

### Dev Environment & Build

This repo uses a Nix flake to pin the .NET 9 SDK. Two dev shells exist:

- Interactive development (loads secrets via 1Password CLI):
	```bash
	nix develop
	```
- Minimal build shell (no secrets, suitable for CI / pure builds):
	```bash
	nix develop .#build -c dotnet build
	```

If you see NETSDK1045 or a .NET version other than 9.x, you are not inside one of the dev shells.

### Running

Set environment variables:

```bash
export AZURE_OPENAI_ENDPOINT="https://<your-resource>.openai.azure.com" 
export AZURE_OPENAI_DEPLOYMENT_NAME="gpt-4o" # or your deployment
```

Then build and run the console project (interactive shell recommended so secrets are present):

```bash
dotnet run --project src/Voice2Action.Console/Voice2Action.Console.csproj
```

For a pure compile (e.g. CI) without needing runtime secrets:

```bash
nix develop .#build -c dotnet build
```

### Next Steps (Ideas)
- Add resilience (Polly) around AI calls
- Introduce streaming token support in drafting
- Implement alternative delivery channels (email SMTP / Graph API)
- Add unit tests for spam vs non‑spam branches

