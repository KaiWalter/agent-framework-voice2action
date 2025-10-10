# Agent Framework Voice‑to‑Action

Primary scenario: an Orchestrator agent interprets a short voice recording (e.g. “Tomorrow morning follow up with Alex about the August numbers and send an email”) and turns intent into concrete actions by dynamically invoking capabilities exposed by independent MCP tool servers. The tool servers themselves have *zero* awareness of workflow or multi‑step planning—they only expose atomic functions. The orchestration logic lives exclusively in the `Voice2Action.Orchestrator` project.

Current capabilities (delivered purely as tools):

* Audio transcription (Whisper / Azure OpenAI)
* Current date/time (for normalizing relative dates)
* Setting reminders (placeholder)
* Sending emails (placeholder)
* Fallback notification (alias email) when no actionable intent is identified

Future capabilities (calendar, task systems, real email, etc.) are added by publishing more tool MCPs—without changing the orchestrator’s planning core.

## Architecture (Refocused on Orchestrator + Tool MCPs)

```
src/
  Voice2Action.Domain/           # Pure contracts + DTOs only
  Voice2Action.Application/      # Orchestration abstractions (e.g. IAgentSetProvider)
  Voice2Action.Infrastructure/   # Concrete service implementations + agent wiring
  Voice2Action.Orchestrator/     # Orchestrator entry: coordinates tool MCPs (primary use case)
  Voice2Action.McpUtility/       # MCP server exposing: TranscribeVoiceRecording, GetCurrentDateTime
  Voice2Action.McpOffice/        # MCP server exposing: SetReminder, SendEmail
  Voice2Action.Console/          # Legacy/simple CLI runner (can call orchestrator directly)
  tests/                         # Integration tests
```

### Separation of Concerns
| Layer / Project | Responsibility | Knows About Workflow? |
|-----------------|----------------|------------------------|
| Domain          | Service & agent interfaces, DTOs | No |
| Infrastructure  | Implements Domain ports; prompt templates | Only generic tool wiring logic |
| MCP Projects (`McpUtility`, `McpOffice`) | Publish atomic tools via MCP (stdio) | No (tools only) |
| Orchestrator    | Interprets user’s intent, plans steps, selects tools | Yes (the *only* planner) |

### MCP Design Principles
1. Tool MCPs expose *only* capability signatures + minimal prompts describing invocation semantics. They never reference end‑to‑end workflow, planning algorithms, or cross‑tool sequencing.
2. Tool prompts must omit any guidance about “next step”, “delegate”, “complete the user’s request”, etc. They simply execute (or refuse with a `CANNOT:` prefix) exactly one tool call.
3. The Orchestrator receives user input (initially a voice recording path) and *derives* required actions. It is not pre‑prompted with hard‑coded knowledge of individual tool behaviors beyond a generic verb taxonomy (see below) and whatever dynamic catalog is injected at runtime.
4. The Orchestrator treats tool outputs as opaque data—no hidden schema assumptions. Robustness goal: adding/removing a tool should require only catalog injection + optional few‑shot updates, not logic rewrites.
5. Capability evolution: Read/lookup tools can be introduced freely; Write/action tools become terminal steps enabling workflow completion.

### Verb Taxonomy (Planning Heuristic)
Extracted from the planning prompt (`coordinator-template.md`) and used conceptually by the Orchestrator. Two buckets:

Intermediate (context‑gathering, non‑terminal): Get, Retrieve, Fetch, Read, Check, Look up

Terminal (action / state‑changing): Set, Schedule, Create, Send, Store, Log, Capture (store), Draft (email), Update, Fallback (notification)

Rules:
* A workflow cannot finish if only intermediate verbs have executed.
* At least one terminal verb must satisfy each actionable intent before completion. If no actionable intent exists, a single Fallback terminal action (notification) must be executed if available.
* Additional verbs may be appended in future; update this list plus prompt examples accordingly.

### High‑Level Orchestration Flow
1. User supplies an audio file path (mp3/wav/m4a) OR future alternative intent source.
2. Orchestrator ensures transcription (Read) → obtains natural language content.
3. Intent extraction: identify candidate terminal actions (e.g. SetReminder, SendEmail) and required supporting reads (GetCurrentDateTime for relative dates).
4. Iterative planning loop: issue exactly one tool call per step via appropriate MCP capability.
5. Completion: after all required terminal intents achieved (or a fallback notification was sent when none were derivable), return concise summary enumerating performed terminal actions.

### Why This Structure
* Strict isolation: MCPs are reusable outside this orchestrated scenario (any MCP client can call them directly) while remaining ignorant of broader workflows.
* Orchestrator remains general: it only “thinks” in terms of Intent → (optional) Read verbs → Terminal verbs.
* Extensibility: adding a calendar tool means publishing a new MCP (or extending an existing one) and updating the dynamic catalog—no orchestration code modifications required.
* Testability: each tool can be faked; orchestrator logic can be unit tested with synthetic tool catalogs.

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

## Running the Orchestrator (Primary)

The orchestrator project (`Voice2Action.Orchestrator`) is the main entrypoint showcasing tool composition. From an interactive dev shell with env vars set:

```bash
nix develop -c dotnet run --project src/Voice2Action.Orchestrator/Voice2Action.Orchestrator.csproj audio-samples/sample-recording-1-task-with-due-date-and-reminder.mp3
```

Legacy/simple console runner (still available):

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
Goal: Publish a capability without leaking process semantics into its prompt.

1. Domain Contract: Define interface in `Voice2Action.Domain` (e.g. `ICalendarService`). Keep it minimal & synchronous where practical for simple tool wrapping.
2. Implementation: Provide a concrete class in `Voice2Action.Infrastructure` (Azure / local / placeholder) returning simple strings or DTOs.
3. MCP Exposure: Add (or extend) an MCP project to surface the new method as a tool. Prompt MUST only describe parameters & return shape.
4. Catalog Wiring: Ensure `DefaultAgentSetProvider` (or successor) registers the tool so the orchestrator’s dynamic catalog includes it. Avoid embedding tool semantics directly in the orchestrator prompt—only its name and short capability descriptor.
5. Verb Mapping: If the capability is read‑only, align it with an intermediate verb (e.g. Get / Retrieve). If it changes state, map to a terminal verb (e.g. Create / Set / Update). Update the verb list in this README and (optionally) planner few‑shots.
6. Tests: Add a fake implementation and planner test scenario ensuring the orchestrator selects the new tool when appropriate.
7. Documentation: Append a bullet to “Current capabilities” above; no orchestrator code edits should be required beyond catalog injection.

If adding many related tools (e.g., calendar read vs write), consider splitting into two MCP servers (Read vs Write) to reinforce the intermediate/terminal action distinction.

## Roadmap / Ideas
* Replace placeholder reminder/email with real integrations (Graph, ToDo, Calendar APIs).
* Add calendar (Read: lookup events; Write: create event) to exercise the intermediate→terminal pattern.
* Retry & resilience policies around LLM calls.
* Guardrails: structured validation and bounded planning iterations.
* Telemetry (timings, token usage) via OpenTelemetry exporters.
* Streaming / incremental UI variant.
* Automatic tool catalog introspection (remove remaining static prompt elements).
* Enhanced evaluation harness for verb classification accuracy.

## MCP Servers (Tool-Only Philosophy)

Two MCP servers exist presently, each intentionally *ignorant* of orchestration or user intents beyond single calls:

1. `Voice2Action.McpUtility`
  * Tools: `TranscribeVoiceRecording(audioPath)`, `GetCurrentDateTime()`
  * Returns raw transcription text or `LOCAL=` / `UTC=` timestamps.
2. `Voice2Action.McpOffice`
  * Tools: `SetReminder(task, dueDate, reminderDate?)`, `SendEmail(subject, body)`, `SendFallbackNotification(subject, body)`
  * Returns plain string acknowledgements.

Their prompts enforce: one tool per request, no narrative, `CANNOT:` prefix on failure. They are reusable by *any* external MCP client independent of the orchestrator.

Example (embedding utility MCP server in a custom host):
```csharp
// Construct domain services then expose via MCP stdio
await UtilityMcpServer.RunAsync(transcriptionService, dateTimeService, CancellationToken.None);
```

Add new MCP servers following the same minimalist prompt pattern.

---
For historical context, an earlier spam/email classification slice was removed in favor of the present multi‑agent voice orchestration; any lingering references in code comments will be gradually cleaned.

