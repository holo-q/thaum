using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Terminal.Gui;
using Thaum.Core.Services;
using Thaum.UI;

namespace Thaum;

public static class Program
{
    public static async Task Main(string[] args)
    {
        var host = Host.CreateDefaultBuilder(args)
            .ConfigureServices(ConfigureServices)
            .ConfigureLogging(logging => logging.AddConsole())
            .UseConsoleLifetime()
            .Build();

        var app = host.Services.GetRequiredService<ThaumApplication>();
        
        try
        {
            await app.RunAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Fatal error: {ex.Message}");
            Environment.Exit(1);
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<ThaumApplication>();
        services.AddSingleton<ILspClientManager, SimpleLspClientManager>();
        services.AddSingleton<ISummarizationEngine, HierarchicalSummarizationEngine>();
        services.AddSingleton<ILlmProvider, HttpLlmProvider>();
        services.AddSingleton<ICacheService, SqliteCacheService>();
        services.AddSingleton<IChangeDetectionService, FileSystemChangeDetectionService>();
        services.AddSingleton<IDependencyTracker, DependencyTracker>();
        services.AddSingleton<IMcpServer, SimpleMcpServer>();
        services.AddHttpClient();
    }
}