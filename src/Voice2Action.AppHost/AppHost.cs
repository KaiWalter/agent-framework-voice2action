using Aspire.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

// Load environment variables for Azure OpenAI if present.
var azureEndpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT");
var azureKey = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY");
var audioDeployment = Environment.GetEnvironmentVariable("AZURE_OPENAI_AUDIO_DEPLOYMENT_NAME") ?? "whisper";

// Utility MCP server project
var utility = builder.AddProject(
    name: "mcp-utility",
    projectPath: "../Voice2Action.McpUtility/Voice2Action.McpUtility.csproj")
    .WithEnvironment("MCP_HTTP_PORT", "5088")
    .WithEndpoint(name: "http", port: 5088, scheme: "http");

if (!string.IsNullOrWhiteSpace(azureEndpoint)) utility.WithEnvironment("AZURE_OPENAI_ENDPOINT", azureEndpoint);
if (!string.IsNullOrWhiteSpace(azureKey)) utility.WithEnvironment("AZURE_OPENAI_API_KEY", azureKey);
utility.WithEnvironment("AZURE_OPENAI_AUDIO_DEPLOYMENT_NAME", audioDeployment);

// OfficeAutomation MCP server project
var office = builder.AddProject(
    name: "mcp-office",
    projectPath: "../Voice2Action.McpOffice/Voice2Action.McpOffice.csproj")
    .WithEnvironment("MCP_HTTP_PORT_OFFICE", "5090")
    .WithEndpoint(name: "http", port: 5090, scheme: "http");

var app = builder.Build();
app.Run();
