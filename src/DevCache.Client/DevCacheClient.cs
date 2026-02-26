using System.Net.Sockets;
using System.Text;

namespace DevCache.Client;

public class DevCacheClient : IDisposable
{
    private readonly TcpClient _tcp;
    private readonly NetworkStream _stream;
    private readonly byte[] _buffer = new byte[8192];

    public DevCacheClient(string host = "127.0.0.1", int port = 6380)
    {
        _tcp = new TcpClient();
        _tcp.Connect(host, port);
        _stream = _tcp.GetStream();
    }

    // ---------------- Basic Commands ----------------
    public async Task<string> SetAsync(string key, string value) =>
        await SendCommandAsync("SET", key, value);

    public async Task<string?> GetAsync(string key)
    {
        string resp = await SendCommandAsync("GET", key);
        if (resp.StartsWith("ERR")) return null;
        return resp;
    }

    public async Task<long> DelAsync(string key) =>
        long.Parse(await SendCommandAsync("DEL", key));

    public async Task<long> IncrAsync(string key) =>
        long.Parse(await SendCommandAsync("INCR", key));

    public async Task<long> DecrAsync(string key) =>
        long.Parse(await SendCommandAsync("DECR", key));

    public async Task<long> IncrByAsync(string key, long increment) =>
        long.Parse(await SendCommandAsync("INCRBY", key, increment.ToString()));

    public async Task<long> ExpireAsync(string key, long seconds) =>
        long.Parse(await SendCommandAsync("EXPIRE", key, seconds.ToString()));

    public async Task<long> PExpireAsync(string key, long milliseconds) =>
        long.Parse(await SendCommandAsync("PEXPIRE", key, milliseconds.ToString()));

    public async Task<long> TTLAsync(string key) =>
        long.Parse(await SendCommandAsync("TTL", key));

    // ---------------- List Commands ----------------
    public async Task<long> LPushAsync(string key, params string[] values)
    {
        string[] args = new[] { key }.Concat(values).ToArray();
        return long.Parse(await SendCommandAsync("LPUSH", args));
    }

    public async Task<long> RPushAsync(string key, params string[] values)
    {
        string[] args = new[] { key }.Concat(values).ToArray();
        return long.Parse(await SendCommandAsync("RPUSH", args));
    }

    public async Task<string?> LPopAsync(string key)
    {
        string resp = await SendCommandAsync("LPOP", key);
        return resp.StartsWith("ERR") ? null : resp;
    }

    public async Task<string?> RPopAsync(string key)
    {
        string resp = await SendCommandAsync("RPOP", key);
        return resp.StartsWith("ERR") ? null : resp;
    }

    public async Task<long> LLenAsync(string key) =>
        long.Parse(await SendCommandAsync("LLEN", key));

    // ---------------- Hash Commands ----------------
    public async Task<int> HSetAsync(string key, params string[] fieldValuePairs)
    {
        string[] args = new[] { key }.Concat(fieldValuePairs).ToArray();
        return int.Parse(await SendCommandAsync("HSET", args));
    }

    public async Task<string?> HGetAsync(string key, string field)
    {
        string resp = await SendCommandAsync("HGET", key, field);
        return resp.StartsWith("ERR") ? null : resp;
    }

    public async Task<bool> HDelAsync(string key, string field) =>
        long.Parse(await SendCommandAsync("HDEL", key, field)) > 0;

    public async Task<long> HLenAsync(string key) =>
        long.Parse(await SendCommandAsync("HLEN", key));

    // ---------------- DB Commands ----------------
    public async Task<string> FlushDbAsync() =>
        await SendCommandAsync("FLUSHDB");

    public async Task<string> FlushAllAsync() =>
        await SendCommandAsync("FLUSHALL");

    public async Task<string[]> KeysAsync(string pattern = "*")
    {
        string resp = await SendCommandAsync("KEYS", pattern);
        // Split by new lines, remove empty entries
        return resp.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
    }

    public async Task<(string Key, string Value)[]> GetAllAsync()
    {
        var keys = await KeysAsync();
        var results = new (string, string)[keys.Length];

        for (int i = 0; i < keys.Length; i++)
        {
            string value = await GetAsync(keys[i]) ?? "";
            results[i] = (keys[i], value);
        }

        return results;
    }

    // ---------------- List Helpers ----------------
    public async Task<string[]> LRangeAsync(string key, int start, int stop)
    {
        string resp = await SendCommandAsync("LRANGE", key, start.ToString(), stop.ToString());
        // Split by lines, remove empty entries
        return resp.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
    }

    // ---------------- Hash Helpers ----------------
    public async Task<(string Field, string Value)[]> HGetAllAsync(string key)
    {
        string resp = await SendCommandAsync("HGETALL", key);
        var lines = resp.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);

        var result = new (string, string)[lines.Length / 2];
        for (int i = 0; i < lines.Length; i += 2)
        {
            result[i / 2] = (lines[i], lines[i + 1]);
        }
        return result;
    }


    // ---------------- Core Sending/Receiving ----------------
    private async Task<string> SendCommandAsync(string command, params string[] args)
    {
        string resp = BuildResp(command, args);
        byte[] bytes = Encoding.UTF8.GetBytes(resp);
        await _stream.WriteAsync(bytes, 0, bytes.Length);

        int read = await _stream.ReadAsync(_buffer, 0, _buffer.Length);
        return Encoding.UTF8.GetString(_buffer, 0, read).Trim();
    }

    private static string BuildResp(string command, params string[] args)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"*{args.Length + 1}");
        sb.AppendLine($"${Encoding.UTF8.GetByteCount(command)}");
        sb.AppendLine(command);
        foreach (var arg in args)
        {
            sb.AppendLine($"${Encoding.UTF8.GetByteCount(arg)}");
            sb.AppendLine(arg);
        }
        return sb.ToString();
    }

    public void Dispose()
    {
        _stream?.Dispose();
        _tcp?.Close();
    }
}

/* USAGE EXAMPLE:  

var client = new DevCacheClient();

// List Example
await client.RPushAsync("mylist", "a", "b", "c");
var listItems = await client.LRangeAsync("mylist", 0, -1);
Console.WriteLine(string.Join(",", listItems)); // Output: a,b,c

// Hash Example
await client.HSetAsync("user", "name", "Alice", "age", "25");
var allFields = await client.HGetAllAsync("user");
foreach (var f in allFields)
    Console.WriteLine($"{f.Field} => {f.Value}"); 
// Output:
// name => Alice
// age => 25

*/
