var builder = DistributedApplication.CreateBuilder(args);

builder.AddDockerComposeEnvironment("compose");

var sql = builder.AddSqlServer("sql")
    .WithImageTag("2022-CU27-ubuntu-22.04")
    .WithEnvironment("MSSQL_PID", "Developer")
    .WithDataVolume("threadly-sql-data");

var database = sql.AddDatabase("threadlydb");

if (builder.ExecutionContext.IsRunMode)
{
    var api = builder.AddProject<Projects.Threadly_Api>("threadly-api", launchProfileName: "https")
        .WithReference(database)
        .WaitFor(database);

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
    var api = builder.AddDockerfile("threadly-api", "../..", "src/Threadly.Api/Dockerfile")
        .WithHttpEndpoint(targetPort: 8080)
        .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Production")
        .WithReference(database)
        .WaitFor(database);

    builder.AddDockerfile("threadly-web", "../../src/threadly-web")
        .WithHttpEndpoint(port: 8080, targetPort: 80)
        .WithEnvironment("API_UPSTREAM", api.GetEndpoint("http"))
        .WaitFor(api)
        .WithExternalHttpEndpoints();
}

builder.Build().Run();
