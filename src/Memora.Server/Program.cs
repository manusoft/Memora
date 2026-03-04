using ManuHub.Memora;
using Microsoft.Extensions.Hosting.WindowsServices;

var builder = Host.CreateApplicationBuilder(args);

if (OperatingSystem.IsWindows() && WindowsServiceHelpers.IsWindowsService())
{
    builder.Services.AddWindowsService(options =>
    {
        options.ServiceName = "MemoraServer";
    });
}

if (OperatingSystem.IsLinux())
{
    builder.Services.AddSystemd();
}

builder.Logging.ClearProviders();
builder.Logging.AddConsole();

if (OperatingSystem.IsWindows())
{
    builder.Logging.AddEventLog(settings =>
    {
        settings.SourceName = "MemoraServer";
    });
}

builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
