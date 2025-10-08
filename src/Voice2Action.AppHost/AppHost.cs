using Aspire.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

var utilityPort = int.TryParse(Environment.GetEnvironmentVariable("MCP_HTTP_PORT_UTILITY"), out var up) && up > 0 ? up : 5010;
var officePort = int.TryParse(Environment.GetEnvironmentVariable("MCP_HTTP_PORT_OFFICE"), out var op) && op > 0 ? op : 5020;

var utility = builder.AddProject<Projects.Voice2Action_McpUtility>("mcp-utility")
    .WithEndpoint(name: "http", port: utilityPort, scheme: "http")
    .WithHttpHealthCheck("/healthz");
var office = builder.AddProject<Projects.Voice2Action_McpOffice>("mcp-office")
    .WithEndpoint(name: "http", port: officePort, scheme: "http")
    .WithHttpHealthCheck("/healthz");

var app = builder.Build();
app.Run();
