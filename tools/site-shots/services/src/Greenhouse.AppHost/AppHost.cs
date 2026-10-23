IDistributedApplicationBuilder builder = DistributedApplication.CreateBuilder(args);

IResourceBuilder<ProjectResource> api = builder.AddProject<Projects.Greenhouse_Api>("api");

builder.AddProject<Projects.Greenhouse_Sensors>("sensors")
    .WithReference(api)
    .WaitFor(api);

builder.Build().Run();
