using CoffeeCommunity.TelemetrySimulator;

var builder = Host.CreateApplicationBuilder(args);
builder.AddServiceDefaults();
builder.Services.AddHttpClient("api", client => client.BaseAddress = new Uri(builder.Configuration["Api:BaseUrl"] ?? "http://api"));
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
