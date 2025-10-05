# Copilot Project Instructions

Concise, project-specific guidance for AI coding agents working in this repository.

## 1. Big Picture
This repo experiments with agentic voice-to-action productivity flows using Microsoft Agent Framework + Azure OpenAI. Current implemented slice focuses on classifying inbound email-like text as spam vs not-spam and (if valid) drafting a professional reply.

Architecture follows a Clean-ish layering:
- `Voice2Action.Domain`: Pure domain contracts + simple DTO models (no external deps). Only place defining interfaces like `ISpamDetectionService`, `IEmailDraftService`, etc.
- `Voice2Action.Application`: Orchestrates a use case (`ProcessIncomingEmail`). Depends only on Domain abstractions. No Azure/OpenAI specifics here.
- `Voice2Action.Infrastructure`: Implements domain ports using Azure OpenAI Agents (`OpenAIAgentSpamDetectionService`, `OpenAIAgentEmailDraftService`) and simple messaging adapters (`ConsoleEmailSender`, `ConsoleSpamDispositionService`).
- `Voice2Action.Console`: Composition root (dependency injection + multi‑agent demo invocation).

Goal: Swap AI/provider logic or add delivery channels without touching Domain/Application code.

## 2. Development Environment & Build
ALWAYS enter an appropriate Nix dev shell before any `dotnet build` or `dotnet run` so the .NET 9 SDK is available. If you skip this you'll hit NETSDK1045 errors.

Dev shells:
```
# Interactive (loads secrets via 1Password CLI shellHook)
nix develop

# Minimal build shell (no secrets) – use for CI or pure compilation
nix develop .#build -c dotnet build
```
Quick verification: `dotnet --version` should start with `9.`. If it shows `8.` you are not in the shell.

Environment variables required at runtime:
```
export AZURE_OPENAI_ENDPOINT="https://<resource>.openai.azure.com"
export AZURE_OPENAI_DEPLOYMENT_NAME="gpt-4o"  # or another deployment
```
## AI Agent Working Guide (Voice2Action)
Focused instructions so an AI agent can be productive immediately.

### 1. Architecture (Clean-ish layering)
- `Voice2Action.Domain`: Interfaces + DTOs only (`ISpamDetectionService`, `IEmailDraftService`, `DetectionResult`, `EmailResponse`). No external SDK types.
- `Voice2Action.Application`: Orchestrator (`ProcessIncomingEmail`) consumes domain ports only.
- `Voice2Action.Infrastructure`: Implements ports (AI: `OpenAIAgentSpamDetectionService`, `OpenAIAgentEmailDraftService`; Messaging: `ConsoleEmailSender`, `ConsoleSpamDispositionService`).
- `Voice2Action.Console`: Composition root (DI + environment config + sample multi‑agent run).
Goal: Swap providers or delivery channels without touching Domain/Application.

### 2. Build & Run
Choose a dev shell (interactive vs build) to ensure .NET 9 is available:
```
# interactive dev (with secrets)
nix develop
# or pure build (no secrets fetched)
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
dotnet run --project src/Voice2Action.Console/Voice2Action.Console.csproj
```
Pure build (no run, no secrets required):
```
nix develop .#build -c dotnet build
```

### 3. Agent / LLM Usage Pattern
- `Program.cs` builds a single `IChatClient` via `new AzureOpenAIClient(uri, new AzureKeyCredential(apiKey)).GetChatClient(deployment).AsIChatClient()`.
- Two `ChatClientAgent` instances registered (ordering currently used to distinguish spam vs drafting).
- Services call `_agent.RunAsync(message, thread: null, options: null, cancellationToken)` using the preview signature `RunAsync(string, AgentThread?, AgentRunOptions?, CancellationToken)`.
- Structured output: each agent sets `ChatOptions.ResponseFormat = ChatResponseFormat.ForJsonSchema<T>`; adapter deserializes `response.Text` and throws if null (fail fast).

### 4. Extending Functionality
Add new external capability:
1. Define port interface in Domain.
2. Consume it in Application (no Azure/OpenAI refs).
3. Implement in Infrastructure (use Azure/OpenAI or other provider).
4. Register in Console DI.
Add another LLM tool: create new `ChatClientAgent` with focused system prompt + JSON schema DTO; register before consumers (or refactor to named registrations to avoid fragile indexing).

### 5. Conventions & Style
- DTOs: simple mutable props, explicit `[JsonPropertyName]` for stable schema.
- Concrete classes marked `sealed`.
- No Azure/OpenAI types leak beyond Infrastructure.
- Maintain fail-fast behavior (throw on invalid JSON) unless adding explicit retry logic.

### 6. Common Pitfalls
- Skipping `nix develop` → NETSDK1045 (wrong SDK version).
- Missing any of the three env vars → startup exception.
- Relying on agent registration order; adding more agents requires refactor (recommend keyed/named registration).
- Preview API drift: `RunAsync` overloads can change—verify against upstream samples.

### 7. Reference Sources (authoritative for signatures)
- Basic conditional workflow: 01 EdgeCondition sample
- Switch/case routing: 02 SwitchCase sample
- Multi-selection edges: 03 MultiSelection sample
- Core OpenAI agent implementation (current method signatures): `OpenAIChatClientAgent.cs`
Links already embedded in earlier revisions; keep them when updating.

### 8. Key Files
- `ProcessIncomingEmail.cs`: Orchestration.
- `AI/*.cs`: LLM adapters.
- `Messaging/ConsoleEmailSender.cs`: Output & spam handling.
- `Program.cs`: DI & agent configuration.
- `flake.nix`: Ensures .NET 9 toolchain inside dev shell.

### 9. Testing (not yet present)
- Use in-memory fakes for domain interfaces; do not reference Azure SDK in tests.

Focus future edits on keeping layering intact, avoiding SDK leakage outside Infrastructure, and updating RunAsync usage only after checking upstream samples.
