namespace DevCache.Service;

public class Worker(ILogger<Worker> logger, IConfiguration configuration) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var server = new DevCacheServer(configuration);

        try
        {
            await server.StartAsync(stoppingToken);
            logger.LogInformation("DevCache server stopped gracefully");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "DevCache failed");
            throw;
        }
        finally
        {
            server.Dispose();
            Console.WriteLine("[SHUTDOWN] All resources disposed");
        }
    }
}
