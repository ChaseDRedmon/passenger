using Greenhouse.Sensors;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
builder.Services.AddServiceDiscovery();
// The API by its resource's name, which the AppHost's service discovery resolves.
builder.Services.AddHttpClient<GreenhouseClient>(client => client.BaseAddress = new Uri("http://api"))
    .AddServiceDiscovery();
builder.Services.AddHostedService<SoilSensors>();

builder.Build().Run();
