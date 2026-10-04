var builder = DistributedApplication.CreateBuilder(args);

builder.AddDockerComposeEnvironment("compose");

if (builder.ExecutionContext.IsRunMode)
{
    var api = builder.AddProject<Projects.Threadly_Api>("threadly-api", launchProfileName: "https");

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
        .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Production");

    builder.AddDockerfile("threadly-web", "../../src/threadly-web")
        .WithHttpEndpoint(port: 8080, targetPort: 80)
        .WithEnvironment("API_UPSTREAM", api.GetEndpoint("http"))
        .WaitFor(api)
        .WithExternalHttpEndpoints();
}

builder.Build().Run();
