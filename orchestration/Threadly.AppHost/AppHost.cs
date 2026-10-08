using Aspire.Hosting.Docker.Resources.ServiceNodes;
using Aspire.Hosting.Pipelines;
using Microsoft.Extensions.DependencyInjection;
using Threadly.AppHost;

var builder = DistributedApplication.CreateBuilder(args);

string proxySubnet = builder.Configuration["Deployment:ProxySubnet"] ?? "172.30.80.0/24";
string proxyAddress = builder.Configuration["Deployment:ProxyAddress"] ?? "172.30.80.10";
var compose = builder.AddDockerComposeEnvironment("compose");
#pragma warning disable ASPIREPIPELINES001, ASPIREPIPELINES004 // Complete the generated Compose network configuration before publish/deploy finishes.
compose.WithPipelineStepFactory("configure-proxy-network", async context =>
{
    string outputPath = context.Services.GetRequiredService<IPipelineOutputService>().GetOutputDirectory();
    string composePath = Path.Combine(outputPath, "docker-compose.yaml");
    string yaml = await File.ReadAllTextAsync(composePath, context.CancellationToken);
    await File.WriteAllTextAsync(composePath, ProxyDeployment.Configure(yaml, proxySubnet, proxyAddress), context.CancellationToken);
}, dependsOn: ["publish-compose"], requiredBy: [WellKnownPipelineSteps.Publish]);
#pragma warning restore ASPIREPIPELINES001, ASPIREPIPELINES004

var sql = builder.AddSqlServer("sql")
    .WithImageTag("2022-CU27-ubuntu-22.04")
    .WithEnvironment("MSSQL_PID", "Developer")
    .WithDataVolume("threadly-sql-data")
    .PublishAsDockerComposeService((_, service) =>
    {
        const string sqlHealthCheckCommand =
            """SQLCMDPASSWORD="$$MSSQL_SA_PASSWORD" /opt/mssql-tools18/bin/sqlcmd """
            + "-S localhost -U sa -C -Q 'SELECT 1' -b -l 3 -o /dev/null";

        service.Healthcheck = new Healthcheck
        {
            Test = ["CMD-SHELL", sqlHealthCheckCommand],
            Interval = "5s",
            Timeout = "5s",
            StartPeriod = "20s",
            Retries = 24
        };
    });

var database = sql.AddDatabase("threadlydb");

if (builder.ExecutionContext.IsRunMode)
{
    var migrations = builder.AddProject<Projects.Threadly_MigrationWorker>("threadly-migrations")
        .WithReference(database)
        .WaitFor(database);

    var api = builder.AddProject<Projects.Threadly_Api>("threadly-api", launchProfileName: "https")
        .WithReference(database)
        .WaitForCompletion(migrations);

    builder.AddJavaScriptApp("threadly-web", "../../src/threadly-web")
        .WithRunScript("start")
        .WithNpm(installCommand: "ci")
        .WithHttpEndpoint(port: 4200, targetPort: 4200, isProxied: false)
        .WithEnvironment("API_HTTPS", api.GetEndpoint("https"))
        .WithReference(api)
        .WaitFor(api)
        .WithExternalHttpEndpoints();
}
else
{
    var siteKey = builder.AddParameter("turnstile-site-key");
    var secretKey = builder.AddParameter("turnstile-secret-key", secret: true);
    var hostname = builder.AddParameter("turnstile-hostname");
    var migrations = builder.AddDockerfile(
        "threadly-migrations", "../..", "src/Threadly.MigrationWorker/Dockerfile")
        .WithReference(database)
        .WaitFor(database)
        .PublishAsDockerComposeService((_, service) =>
        {
            service.DependsOn[sql.Resource.Name].Condition = "service_healthy";
            service.Restart = "no";
        });

    var api = builder.AddDockerfile("threadly-api", "../..", "src/Threadly.Api/Dockerfile")
        .WithHttpEndpoint(targetPort: 8080)
        .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Production")
        .WithEnvironment("Turnstile__SiteKey", siteKey)
        .WithEnvironment("Turnstile__SecretKey", secretKey)
        .WithEnvironment("Turnstile__AllowedHostnames__0", hostname)
        .WithEnvironment("Turnstile__TestMode", "false")
        .WithEnvironment("Turnstile__ExpectedAction", builder.Configuration["Turnstile:ExpectedAction"] ?? "comment_create")
        .WithEnvironment("Turnstile__Timeout", builder.Configuration["Turnstile:Timeout"] ?? "00:00:10")
        .WithEnvironment("PublicationRateLimit__PermitLimit", builder.Configuration["PublicationRateLimit:PermitLimit"] ?? "10")
        .WithEnvironment("PublicationRateLimit__Window", builder.Configuration["PublicationRateLimit:Window"] ?? "00:01:00")
        .WithEnvironment("TrustedProxies__KnownProxies__0", proxyAddress)
        .WithEnvironment("TrustedProxies__ForwardLimit", "1")
        .WithReference(database)
        .WaitForCompletion(migrations);

    builder.AddDockerfile("threadly-web", "../../src/threadly-web")
        .WithHttpEndpoint(port: 8080, targetPort: 80)
        .WithEnvironment("API_UPSTREAM", api.GetEndpoint("http"))
        .WaitFor(api)
        .WithExternalHttpEndpoints();
}

builder.Build().Run();
