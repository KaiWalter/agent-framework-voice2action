# Copilot Project Instructions

Concise, project-specific guidance for AI coding agents working in this repository.

## 1. Big Picture
This repo implements a multi‑agent voice‑to‑action workflow with Microsoft Agent Framework (preview) + Azure OpenAI. A planner (coordinator) and several worker agents transform an audio recording into concrete actions (transcription, reminder creation, email drafting/sending placeholder) and a final summary.

Clean layering:
- `Voice2Action.Domain`: Pure contracts (`ITranscriptionService`, `IDateTimeService`, `IReminderService`, `IEmailService`, `ITextAgent`, `IVoiceCommandOrchestrator`) + DTOs (`AgentActionRecord`, `OrchestrationResult`). No Azure/OpenAI types.
- `Voice2Action.Application`: Orchestration abstractions (e.g. `IAgentSetProvider`) and coordination logic (planner loop lives in Infrastructure currently but only uses domain contracts).
- `Voice2Action.Infrastructure`: Implementations of domain ports (Azure OpenAI transcription, simple date/time, reminder, email services) + agent assembly (`DefaultAgentSetProvider`) + orchestrator implementation.
- `Voice2Action.Console`: Composition root / CLI entry (wires env vars, builds agent set, runs orchestrator).

Goal: Add/replace tools by introducing domain interfaces + implementations without modifying orchestration core logic.

## 2. Development Environment & Build
ALWAYS enter an appropriate Nix dev shell before any `dotnet build` or `dotnet run` so the .NET 9 SDK is available. If you skip this you'll hit NETSDK1045 errors.

Dev shells:
```
# Interactive (secrets, manual runs)
nix develop

# Pure build/test (CI safe, no secrets)
nix develop .#build -c dotnet build
nix develop .#build -c dotnet test
```
Always use one of these; otherwise you’ll hit NETSDK1045 (.NET 9 target with .NET 8 SDK).

Environment variables:
```
export AZURE_OPENAI_ENDPOINT="https://<resource>.openai.azure.com"
export AZURE_OPENAI_API_KEY="<key>"
export AZURE_OPENAI_DEPLOYMENT_NAME="gpt-4o"
export AZURE_OPENAI_AUDIO_DEPLOYMENT_NAME="whisper"
```
## AI Agent Working Guide (Voice2Action)
Focused instructions so an AI agent can be productive immediately.

### 1. Architecture Snapshot
- Domain: service interfaces (transcription, datetime, reminder, email), agent/orchestration contracts.
- Infrastructure: service implementations + planner/worker LLM agent wiring (`DefaultAgentSetProvider`).
- Console: thin entrypoint.
No legacy spam/email code remains.

### 2. Build & Run
Use dev shell to build with .NET 9:
```
nix develop .#build -c dotnet build
```
Env vars (API key only – CLI auth removed):
```
export AZURE_OPENAI_ENDPOINT="https://<resource>.openai.azure.com"
export AZURE_OPENAI_API_KEY="<key>"
export AZURE_OPENAI_DEPLOYMENT_NAME="gpt-4o" # or your deployment
```
Run sample (needs secrets → use interactive shell):
```
nix develop -c dotnet run --project src/Voice2Action.Console/Voice2Action.Console.csproj
```

### 3. Agent / LLM Usage Pattern
- Each worker agent: `ChatClientAgent` with `ChatOptions.Tools` populated via `AIFunctionFactory.Create` wrapping domain service methods.
- Coordinator agent: prompt injects a catalog of worker names + capabilities; returns minified JSON (`{"Action":"DELEGATE|DONE","Agent":"...","Task":"...","Summary":"..."}`).
- Orchestrator loop (Infrastructure) parses planner JSON and invokes the selected worker.
- Tool calls execute synchronously against domain services (fail-fast exceptions bubble unless caught by model behavior).

### 4. Extending Functionality
Add a new tool capability:
1. Add interface to Domain (e.g. `ICalendarService`).
2. Implement it in Infrastructure.
3. Inject into `DefaultAgentSetProvider` and expose with `ChatOptions.Tools` on an existing or new worker agent.
4. Update prompts (worker + coordinator template) to include the new capability.
5. (Optional) Add integration / unit tests with a fake implementation.

### 5. Conventions & Style
- Domain DTOs/classes `sealed`, mutable auto props.
- Keep prompts in `Prompts/` folder (Console project) – provider loads by file name.
- No Azure/OpenAI types outside Infrastructure.
- Prefer small focused worker prompts; coordinator prompt template token-replaced with agent catalog.
- Fail-fast: if planner returns invalid JSON, orchestrator should surface error (future: structured retry/backoff).

### 6. Common Pitfalls
- Forgetting Nix dev shell → NETSDK1045.
- Not providing both chat and audio deployment env vars.
- Planner prompt drift (JSON not minified / extra commentary) – reinforce in template.
- Tool signature mismatches after interface changes (update `AIFunctionFactory.Create` signatures accordingly).

### 7. Reference Sources (authoritative for signatures)
- Basic conditional workflow: 01 EdgeCondition sample
- Switch/case routing: 02 SwitchCase sample
- Multi-selection edges: 03 MultiSelection sample
- Core OpenAI agent implementation (current method signatures): `OpenAIChatClientAgent.cs`
Links already embedded in earlier revisions; keep them when updating.

### 8. Key Files
- `Domain/Ports.cs`: Service + agent orchestration interfaces.
- `Domain/OrchestrationModels.cs`: Action & result models, `ITextAgent`, `AgentSet`.
- `Infrastructure/AI/DefaultAgentSetProvider.cs`: Builds coordinator + workers.
- `Infrastructure/AI/OpenAIAudioTranscriptionService.cs`: Whisper transcription.
- `Infrastructure/AI/DateTimeService|ReminderService|EmailService.cs`: Tool service implementations.
- `Infrastructure/AI/VoiceCommandOrchestrator*.cs` (if present): Planner execution loop.
- `Console/Program.cs`: Composition root.
- `flake.nix`: Nix dev shell definition.
- `tests/Voice2Action.IntegrationTests/*`: Transcription integration test.

### 9. Testing
- Integration test uses fake transcription when real creds absent.
- Add future tests: planner loop with fake agents; tool services simple string assertions.

### 10. Build & Test Enforcement
Always run builds & tests via Nix:
```
nix develop .#build -c dotnet build
nix develop .#build -c dotnet test
```
Direct `dotnet build` outside shell is considered invalid and should be corrected.

Focus future edits on keeping layering intact, avoiding SDK leakage outside Infrastructure, and updating RunAsync usage only after checking upstream samples.
