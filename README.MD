# Agent Framework Voice‑to‑Action

Experimental multi‑agent voice productivity workflow using the (preview) Microsoft Agent Framework + Azure OpenAI. A short voice recording (e.g. “Tomorrow morning follow up with Alex about the August numbers and send an email”) is transcribed and then iteratively planned and executed by a coordinator + worker agents that have tool access for:

* Audio transcription (Whisper / Azure OpenAI)
* Date/Time normalization
* Setting reminders (placeholder implementation)
* Sending emails (placeholder implementation)

Future tools (calendar, task systems, real email, etc.) can be added without touching the core orchestration logic.

## Current Architecture

Clean‑ish layered structure:

```
src/
  Voice2Action.Domain/          # Pure domain contracts + DTOs (no external deps)
  Voice2Action.Application/     # Orchestration abstractions (e.g. IAgentSetProvider) – depends on Domain only
  Voice2Action.Infrastructure/  # Azure OpenAI + tool service implementations
  Voice2Action.Console/         # Composition root / CLI entry
  tests/                        # Integration tests (transcription currently)
```

### Domain Layer
Interfaces:
* `ITranscriptionService`
* `IDateTimeService`
* `IReminderService`
* `IEmailService`
* Multi‑agent orchestration contracts: `ITextAgent`, `AgentSet`, `IVoiceCommandOrchestrator`, plus result models.

### Infrastructure Layer
Implements the domain ports using Azure OpenAI (`OpenAIAudioTranscriptionService`) and simple in‑memory/string placeholder services (`DateTimeService`, `ReminderService`, `EmailService`). It also assembles LLM agents in `DefaultAgentSetProvider`:

* Utility agent (transcription + current time) exposing tools via `ChatOptions.Tools`.
* OfficeAutomation agent (reminder + email) likewise exposing tools.
* Coordinator agent (planner) producing minified JSON directives `{Action,Agent,Task,Summary}` consumed by the orchestrator loop.

### Orchestration Flow
1. CLI passes absolute audio path to orchestrator.
2. Planner iteratively decides to DELEGATE to a worker with a task (e.g. `TranscribeVoiceRecording(/abs/path.mp3)` or `SetReminder(...)`).
3. Worker agent executes tool (domain service) directly through registered LLM tool functions.
4. Planner eventually returns `DONE` with a summary.
5. All actions recorded in chronological order.

### Why This Structure
* Domain remains pure → easy to mock in tests.
* Adding a new capability means: define interface in Domain, implement in Infrastructure, register tool binding (no orchestrator changes unless planner prompt needs updating).
* Avoids leaking Azure SDK types outside Infrastructure.

## Development Environment

The repository pins the .NET 9 SDK via a Nix flake. Always build and test inside a dev shell or you’ll hit `NETSDK1045`.

Interactive (with secrets / environment hooks):
```bash
nix develop
```

Pure build / CI (no secrets needed):
```bash
nix develop .#build -c dotnet build
```

Verify SDK:
```bash
dotnet --version  # should start with 9.
```

## Required Environment Variables

```bash
export AZURE_OPENAI_ENDPOINT="https://<resource>.openai.azure.com"
export AZURE_OPENAI_API_KEY="<key>"
export AZURE_OPENAI_DEPLOYMENT_NAME="gpt-4o"            # chat / reasoning model
export AZURE_OPENAI_AUDIO_DEPLOYMENT_NAME="whisper"      # transcription model
```

## Build & Test (Enforced via Nix Shell)

Always invoke build & test like:
```bash
nix develop .#build -c dotnet build
nix develop .#build -c dotnet test
```

`dotnet build` outside the shell (showing 8.x SDK) is unsupported.

## Running the Console

From an interactive shell (so keys are present):
```bash
nix develop -c dotnet run --project src/Voice2Action.Console/Voice2Action.Console.csproj audio-samples/sample-recording-1-task-with-due-date-and-reminder.mp3
```

Output lists each agent action and the final summary.

## Testing Strategy

Current: Integration test for transcription (`AudioTranscriptionIntegrationTests`). It can run with a fake transcription by supplying placeholder env values or `USE_FAKE_TRANSCRIPTION_FOR_TESTS=true`.

Planned enhancements:
* Unit tests for planner loop with fake agents.
* Tests for reminder/email tool bindings (pure string assertions).

## Adding a New Tool Capability
1. Add interface to `Voice2Action.Domain` (e.g. `ICalendarService`).
2. Implement in `Voice2Action.Infrastructure`.
3. Inject into `DefaultAgentSetProvider` constructor and expose via `ChatOptions.Tools` on a worker agent.
4. Update the worker prompt & coordinator prompt catalog injection.
5. (Optional) Extend planner prompt examples to teach when to call it.

## Roadmap / Ideas
* Replace placeholder reminder/email with real integrations (Graph, ToDo, Calendar APIs).
* Retry & resilience policies around LLM calls.
* Structured evaluation of planner termination (guard against loops).
* Telemetry (timings, token usage) via OpenTelemetry exporters.
* Add streaming / incremental UI variant.

---
For historical context, an earlier spam/email classification slice was removed in favor of the present multi‑agent voice orchestration; any lingering references in code comments will be gradually cleaned.

