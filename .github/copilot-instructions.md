# Copilot Project Instructions

Concise, project-specific guidance for AI coding agents working in this repository.

## 1. Big Picture
This repo experiments with agentic voice-to-action productivity flows using Microsoft Agent Framework + Azure OpenAI. Current implemented slice focuses on classifying inbound email-like text as spam vs not-spam and (if valid) drafting a professional reply.

Architecture follows a Clean-ish layering:
- `Voice2Action.Domain`: Pure domain contracts + simple DTO models (no external deps). Only place defining interfaces like `ISpamDetectionService`, `IEmailDraftService`, etc.
- `Voice2Action.Application`: Orchestrates a use case (`ProcessIncomingEmail`). Depends only on Domain abstractions. No Azure/OpenAI specifics here.
- `Voice2Action.Infrastructure`: Implements domain ports using Azure OpenAI Agents (`OpenAIAgentSpamDetectionService`, `OpenAIAgentEmailDraftService`) and simple messaging adapters (`ConsoleEmailSender`, `ConsoleSpamDispositionService`).
- `Voice2Action.Console`: Composition root (dependency injection + runtime wiring) and a minimal demo invocation.

Goal: Swap AI/provider logic or add delivery channels without touching Domain/Application code.

## 2. Development Environment & Build
ALWAYS enter the Nix dev shell before any `dotnet build` or `dotnet run` so the .NET 9 SDK is available. If you skip this you'll hit NETSDK1045 errors.

Start shell (one time per terminal session):
```
nix develop
```
Quick verification: `dotnet --version` should start with `9.`. If it shows `8.` you are not in the shell.

Environment variables required at runtime:
```
export AZURE_OPENAI_ENDPOINT="https://<resource>.openai.azure.com"
export AZURE_OPENAI_DEPLOYMENT_NAME="gpt-4o"  # or another deployment
```
Build & run the console sample:
```
dotnet run --project src/Voice2Action.Console/Voice2Action.Console.csproj
```
If build errors complain about net9 support, you are NOT inside the nix dev shell.

## 3. Key Patterns
- Dependency Inversion: Application depends only on interfaces in Domain. Infrastructure provides concrete Azure/OpenAI-powered implementations.
- Agent Usage: Agents created in `Program.cs` using `AzureOpenAIClient(...).GetChatClient(deployment).AsIChatClient()` and wrapped by `ChatClientAgent`. Two distinct agents registered (spam detection + drafting) then selected by ordering when injected.
- JSON Structured Outputs: `ChatOptions.ResponseFormat = ChatResponseFormat.ForJsonSchema<T>` ensures LLM returns JSON matching `DetectionResult` or `EmailResponse`. Services deserialize `response.Text` via `JsonSerializer.Deserialize<T>()` and throw if null.
- Simple Console Adapters: Output is side-effected through `Console.WriteLine` in messaging classes—replace these to integrate real channels.

Reference samples for current Agent Framework call signatures:
If you need to double‑check evolving preview APIs (e.g., `AIAgent.RunAsync` parameters, workflow edges, switch/case semantics), consult the official Microsoft Agent Framework getting started samples. Treat them as the authoritative source if this repo's usage ever drifts or compilation errors suggest signature changes:
	- Conditional Edge (basic): https://github.com/microsoft/agent-framework/blob/main/dotnet/samples/GettingStarted/Workflows/ConditionalEdges/01_EdgeCondition/Program.cs
	- Switch / Case routing: https://github.com/microsoft/agent-framework/blob/main/dotnet/samples/GettingStarted/Workflows/ConditionalEdges/02_SwitchCase/Program.cs
	- Multi-selection edges: https://github.com/microsoft/agent-framework/blob/main/dotnet/samples/GettingStarted/Workflows/ConditionalEdges/03_MultiSelection/Program.cs
	- Core OpenAI agent implementation (latest method signatures such as `RunAsync` overloads): https://github.com/microsoft/agent-framework/blob/main/dotnet/src/Microsoft.Agents.AI.OpenAI/OpenAIChatClientAgent.cs

When preview package updates introduce breaking signature changes (e.g., replacing a simple `RunAsync(string, CancellationToken)` with an overload taking `AgentThread?` and `AgentRunOptions?`), prefer adapting to the new form shown in those samples instead of guessing property names.

## 4. Adding New Capabilities (Follow These Conventions)
When adding a new external interaction:
1. Define a new interface in `Voice2Action.Domain` (port).
2. Use it inside an Application-layer use case (or extend existing one) without referencing Azure/OpenAI specifics.
3. Implement in Infrastructure (keep Azure/OpenAI SDK usage here only).
4. Wire it in `Program.cs` (Console) or future host.

When adding another LLM-backed agent tool:
- Create a new `ChatClientAgent` with a focused system prompt and `ChatResponseFormat.ForJsonSchema<YourDto>`.
- Add matching DTO with explicit `JsonPropertyName` attributes if field names should be snake_case.
- Register via DI before services that depend on it.

## 5. Safety & Error Handling Expectations
- Services currently throw on invalid JSON. Keep that pattern (fail fast) unless adding structured retry logic.
- Validate string inputs (`string.IsNullOrWhiteSpace`) before invoking AI calls (see `ProcessIncomingEmail`).

## 6. Testing Guidance (Not Yet Implemented)
- For unit tests, replace concrete services with in-memory fakes implementing the domain interfaces.
- Avoid referencing `Azure.AI.OpenAI` in test projects—mock at the `ISpamDetectionService` / `IEmailDraftService` boundary.

## 7. Common Pitfalls
- Forgetting `nix develop` -> net9 target build failure (NETSDK1045).
- Registering agents: Order matters in current simplistic approach (`GetServices<AIAgent>().ToList()[index]`). If you add more agents, refactor to named registrations to avoid brittle indexing.
- Missing env vars -> startup `InvalidOperationException` in `Program.cs`.

## 8. Suggested Improvements (Optional Backlog Indicators)
Documented but not yet implemented—do not assume they exist:
- Resilience (Polly), streaming token support, alternate delivery channels, unit tests.

## 9. File Landmarks
- `src/Voice2Action.Application/ProcessIncomingEmail.cs`: Orchestration logic.
- `src/Voice2Action.Infrastructure/AI/*.cs`: LLM-backed service adapters.
- `src/Voice2Action.Infrastructure/Messaging/ConsoleEmailSender.cs`: Outbound side-effect adapters.
- `src/Voice2Action.Console/Program.cs`: DI setup & example execution.
- `flake.nix`: Dev environment ensuring correct .NET SDK.

## 10. Style & Conventions
- Keep DTOs simple, mutable (auto-properties) with explicit `JsonPropertyName` for cross-language/LLM clarity.
- Prefer `sealed` for concrete service classes.
- Keep Application layer free of Azure/OpenAI types—only domain interfaces & models.

Use this document to align edits with the layering goals and to avoid leaking infrastructure concerns into core logic. Keep changes minimal, additive, and aligned with existing patterns.
