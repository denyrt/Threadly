using Aspire.Hosting.Docker.Resources.ServiceNodes;

var builder = DistributedApplication.CreateBuilder(args);

builder.AddDockerComposeEnvironment("compose");

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
        .WithReference(database)
        .WaitForCompletion(migrations);

    builder.AddDockerfile("threadly-web", "../../src/threadly-web")
        .WithHttpEndpoint(port: 8080, targetPort: 80)
        .WithEnvironment("API_UPSTREAM", api.GetEndpoint("http"))
        .WaitFor(api)
        .WithExternalHttpEndpoints();
}

builder.Build().Run();
