var builder = DistributedApplication.CreateBuilder(args);

builder.AddProject<Projects.Threadly_Api>("threadly-api");

builder.Build().Run();
