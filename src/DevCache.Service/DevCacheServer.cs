using System.Net;
using System.Net.Sockets;

namespace DevCache.Service;

public sealed class DevCacheServer : IDisposable
{
    private readonly IConfiguration _config;
    private TcpListener? _listener;
    private readonly InMemoryStore _store = new();

    public DevCacheServer(IConfiguration config)
    {
        _config = config;
        CommandRegistry.Initialize(_store);
    }

    public async Task StartAsync(CancellationToken token)
    {
        var bind = _config["DevCache:BindAddress"] ?? "127.0.0.1";
        var port = _config.GetValue<int>("DevCache:Port", 6380);

        _listener = new TcpListener(IPAddress.Parse(bind), port);
        _listener.Start();

        Console.WriteLine("DevCache listening on 127.0.0.1:6380");

        while (!token.IsCancellationRequested)
        {
            var client = await _listener.AcceptTcpClientAsync(token);
            _ = HandleClientAsync(client, token);   // pass token if you want to propagate
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken token)
    {
        using var stream = client.GetStream();

        var reader = new RespReader(stream);
        var writer = new RespWriter();

        try
        {
            while (true)
            {
                var request = await reader.ReadAsync();
                if (request == null)
                    break; // client disconnected

                // Expect array: [command, arg1, arg2...]
                if (request.Type != RespType.Array)
                {
                    //if (!runInBackground)
                        Console.WriteLine("Protocol error: expected array");

                    await writer.WriteAsync(stream,
                        RespValue.Error("Protocol error: expected array"));
                    continue;
                }

                var items = (IReadOnlyList<RespValue>)request.Value!;
                var commandName = ((string?)items[0].Value)?.ToUpperInvariant();

                //if (!runInBackground)
                    Console.WriteLine($"Command Received: {commandName}");

                if (string.IsNullOrWhiteSpace(commandName))
                {
                    await writer.WriteAsync(stream,
                        RespValue.Error("ERR empty command"));
                    continue;
                }

                var args = items
                    .Skip(1)
                    .Select(v => (string?)v.Value ?? string.Empty)
                    .ToList();

                if (!CommandRegistry.TryGet(commandName, out var command))
                {
                    await writer.WriteAsync(stream,
                        RespValue.Error($"ERR unknown command '{commandName}'"));
                    continue;
                }

                var context = new CommandContext
                {
                    Client = client,
                    Stream = stream,
                    Writer = writer
                };

                await command(context, args);

            }
        }
        catch (Exception ex)
        {
            //if (!runInBackground)
                Console.WriteLine($"Client error: {ex.Message}");
        }
        finally
        {
            client.Close();
        }
    }

    public void Dispose()
    {
        _listener?.Stop();
        _store.Dispose();
    }
}
