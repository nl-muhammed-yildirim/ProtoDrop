using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using wa.workers;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureAppConfiguration(config => config.AddEnvironmentVariables())
    .ConfigureServices((context, services) => services.AddWorkerServices(context.Configuration))
    .Build();

await host.RunAsync();
