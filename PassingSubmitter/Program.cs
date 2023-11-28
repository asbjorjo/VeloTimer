using Microsoft.Extensions.Hosting;
using VeloTimer.PassingLoader;

Console.WriteLine(System.Runtime.InteropServices.RuntimeInformation.OSDescription);
Console.WriteLine(System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription);
Console.WriteLine(System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier);

IHostBuilder hostBuilder = Host
    .CreateDefaultBuilder(args)
    .ConfigurePassingLoader();

using IHost host = hostBuilder.Build();

await host.RunAsync();