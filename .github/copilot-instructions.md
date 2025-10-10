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

### 11. Prompts, Tool Usage & Separation of Concerns (Current Requirements)

This repository now enforces a stricter separation between the orchestration (planner loop) and the MCP tool servers. Follow these rules when adding or modifying functionality:

#### 11.1 Coordinator (Planner) Prompt Contract
The coordinator prompt must:
1. Remain GENERIC: it must not list concrete tool names, parameter names, or implementation details beyond the injected agent capability catalog.
2. Always output STRICT, MINIFIED JSON (single line, no commentary) of the form:
	{"Action":"DELEGATE|DONE","Agent":"<WorkerName|empty_if_DONE>","Task":"<ToolInvocationOrPlanningNote>","Summary":"<Optional running summary or final summary when DONE>"}
3. Only two actions are valid:
	- DELEGATE: Provide Agent + Task.
	- DONE: Provide Summary only (Agent may be empty or omitted).
4. Avoid redundant retrieval loops (e.g. repeatedly calling time/date). The orchestrator will nudge the planner if it repeats the same retrieval 3 times.
5. When no actionable terminal intent (schedule/send/create/update) is extracted from the transcript, request the fallback notification tool with an explicit subject/body. If omitted, the orchestrator may auto-inject.

#### 11.2 Worker (MCP) Prompt Contracts
Each worker prompt:
1. Enumerates ONLY the tools that worker exposes with concise, action‑oriented descriptions.
2. Instructs the model to call exactly ONE tool per response (no multi-tool bundling).
3. Produces a direct tool call in the textual form expected by Agents framework (already handled by infrastructure) – we do not add extra narrative around tool invocations.
4. Does not summarize or plan; planning is exclusive to the coordinator.

#### 11.3 Structured Tool Output Envelope
All MCP tool implementations must return a JSON envelope:
  { "ok": true|false, "type": "<CamelCaseType>", "data": { ... }?, "error": { "code": "...", "message": "..." }? }

Rules:
1. Success responses: ok=true, include a semantic "type" (e.g. "Transcription", "Reminder", "Email", "FallbackNotification"). Place result fields inside data.
2. Error responses: ok=false, keep type (if known) and populate error object.
3. Do NOT nest raw model commentary; the tool host code constructs this JSON directly.
4. Helper: use ToolResultJson.Ok(type, anonObject) and ToolResultJson.Error(code, message, type?) for consistency.

#### 11.4 Orchestrator Responsibilities (and ONLY these)
The orchestrator:
1. Loads prompts, builds planner & worker agents with temperature=0 (deterministic output).
2. Injects agent capability catalog into coordinator template.
3. Executes planner loop parsing JSON responses strictly.
4. Delegates tasks verbatim to selected worker.
5. Parses structured tool outputs to capture transcription text and terminal action metadata.
6. Auto-injects fallback notification arguments (subject/body) if planner omits them.
7. Adds a repetition guard message if planner repeats the identical retrieval tool 3 times.
8. Maintains an action log for auditing (AgentActionRecord list).

It MUST NOT:
- Contain domain business logic (e.g. computing due dates, parsing natural dates, generating email content beyond fallback subject trimming).
- Use Azure/OpenAI specific types outside the orchestrator layer where not already necessary for agent creation.
- Hard-code tool names inside the coordinator prompt (only the catalog injection is permitted).

#### 11.5 MCP Server Responsibilities
MCP servers (Utility, Office) must:
1. Encapsulate the actual implementation of tool actions (transcription, reminder, email/fallback) using domain service interfaces.
2. Serialize outputs via ToolResultJson helper; never leak exceptions (catch and wrap into error envelope).
3. Keep parameter naming stable; if renaming parameters, update: interface signature, tool host method, and any dependent planner examples/tests.
4. Avoid referencing orchestrator heuristics or planner logic.

#### 11.6 Fallback Notification Policy
Trigger fallback when transcript contains useful information but no explicit terminal intent (no clear schedule/send/create/update). Requirements:
1. Provide subject (short summary ≤ ~60 chars) and body (full transcript or distilled note).
2. If planner supplies empty parentheses, orchestrator auto-fills using transcript (subject = first ~7 words, body = full transcript).
3. Tool must error if body empty (guard to avoid silent no-op); orchestrator auto-fill prevents this path.

#### 11.7 Tests & Enforcement
Current tests enforcing these contracts should be maintained / extended:
1. Prompt validation tests: assert coordinator prompt contains JSON contract cues and remains generic.
2. ToolResultJson tests: assert envelope shape and error behavior (e.g. fallback missing body -> ok=false).
3. (Add soon) Planner loop test with fake agents verifying:
	- Repetition guard injection when same retrieval >3 times.
	- Auto-injected fallback arguments produce success envelope.

#### 11.8 Adding a New Tool Capability – Checklist (Extended)
1. Domain: Add interface + DTOs if required.
2. Infrastructure: Implement service.
3. MCP: Expose new tool method returning envelope via ToolResultJson.
4. Worker prompt: Add succinct description of tool; keep style consistent.
5. Coordinator template: NO changes unless new capability class requires new generic verb guidance (avoid specific names).
6. Tests: Add envelope test + (optional) planner decision test using fake worker.
7. Orchestrator: Only update if you need to parse new structured result to drive subsequent planning context (avoid embedding special-case logic otherwise).

#### 11.9 Determinism & Robustness Guidelines
1. Temperature=0 for planner and workers.
2. Keep tool descriptions concise to reduce token drift.
3. Enforce minimal JSON (no whitespace/line breaks) in planner output.
4. When modifying prompts, re-run prompt validation tests or add new assertions mirroring the JSON contract.
5. If planner produces malformed JSON, orchestrator should feed back an error string and attempt a corrected loop (already implemented).

#### 11.10 Common Failure Patterns & Mitigations
| Issue | Symptom | Mitigation |
|-------|---------|------------|
| Nested JSON string from model | Transcript shows escaped JSON | Orchestrator unwrapping (already in place); retain when refactoring. |
| Repeated retrieval loop | Planner delegates same tool indefinitely | Repetition guard message prompts planner to progress or DONE. |
| Missing fallback args | Tool error: body required | Orchestrator auto-argument injection. |
| Tool signature drift | Runtime invocation errors | Update all: interface, host method, prompts, tests. |
| Prompt leakage of concrete tools | Reduced generality / overfitting | Keep coordinator generic; only capability catalog injection. |

Adhering to this section ensures future feature additions do not erode the clean separation and deterministic behavior now in place.
