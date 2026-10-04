using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Threadly.Infrastructure;

namespace Threadly.MigrationWorker;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

        builder.AddServiceDefaults();
        builder.Services.AddInfrastructure();

        builder.Services.AddSingleton<MigrationService>();
        builder.Services.AddHostedService(services => services.GetRequiredService<MigrationService>());

        using IHost host = builder.Build();
        MigrationService migrationService = host.Services.GetRequiredService<MigrationService>();

        await host.RunAsync();

        return migrationService.ExitCode;
    }
}
