using DataMcpServer.Services;

var builder = WebApplication.CreateBuilder(args);

// Data service: LocalFiles (default) or PortalApi (set DataSource in appsettings)
var dataSource = builder.Configuration["DataSource"] ?? "LocalFiles";
if (dataSource.Equals("PortalApi", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddHttpClient("portal");
    builder.Services.AddSingleton<ITransactionDataService, PortalApiTransactionDataService>();
    builder.Services.AddSingleton<IKycDataService, PortalApiKycDataService>();
}
else
{
    builder.Services.AddSingleton<ITransactionDataService, LocalTransactionDataService>();
    builder.Services.AddSingleton<IKycDataService, LocalFileKycDataService>();
}

builder.Services.AddSingleton<EvidenceExtractorService>();

builder.Services
    .AddMcpServer()
    .WithHttpTransport()
    .WithToolsFromAssembly();

var app = builder.Build();

app.MapMcp("/mcp");

app.Run();
