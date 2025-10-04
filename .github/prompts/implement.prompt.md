---
mode: agent
---
Add high level processing only in an AI agentic style as these samples of the framework show:

https://github.com/microsoft/agent-framework/blob/main/dotnet/samples/GettingStarted/Workflows/ConditionalEdges/01_EdgeCondition/Program.cs

https://github.com/microsoft/agent-framework/blob/main/dotnet/samples/GettingStarted/Workflows/ConditionalEdges/02_SwitchCase/Program.cs

For accessing external APIs, suggest environment variables for API keys, URLs or other elements needed to connect to the external service.

Use the Clean Architecture layering as described in the README.md and .github/copilot-instructions.md files.

Use the existing interfaces in the Domain layer to define new capabilities, and implement them in the Infrastructure layer.

Use the existing agents as examples of how to implement new agents.