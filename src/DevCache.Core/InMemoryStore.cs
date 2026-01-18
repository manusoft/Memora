using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;

namespace DevCache;

public sealed class InMemoryStore : IDisposable
{
    private readonly ConcurrentDictionary<string, ValueEntry> _data =
       new(StringComparer.OrdinalIgnoreCase);

    private readonly CancellationTokenSource _cts = new();
    private readonly Task _expiryTask;

    private sealed class ValueEntry
    {
        public string Value = default!;
        public DateTime? ExpiryUtc; // UTC time
        public string Type => "string"; // for future type support
        public int Size => Value?.Length ?? 0;

        public long GetTtlSeconds()
        {
            if (!ExpiryUtc.HasValue) return -1;
            var ttl = (long)(ExpiryUtc.Value - DateTime.UtcNow).TotalSeconds;
            return ttl > 0 ? ttl : -2;
        }
    }

    private readonly string _aofPath;
    private StreamWriter? _aofWriter;
    private readonly object _aofLock = new();

    public InMemoryStore()
    {
        _aofPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DevCache", "devcache.aof");

        try
        {
            // Ensure directory exists
            Directory.CreateDirectory(Path.GetDirectoryName(_aofPath)!);

            _aofWriter = new StreamWriter(_aofPath, true, Encoding.UTF8)
            {
                AutoFlush = true
            };

            //// Optional: write startup marker
            _aofWriter.WriteLine("# DevCache AOF started at " + DateTime.Now.ToString("o"));
            _aofWriter.Flush();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AOF INIT ERROR] Cannot open AOF file: {ex.Message}");
            _aofWriter = null; // or throw if you want to fail fast
        }

        // Load from AOF if exists
        //LoadAof();

        _expiryTask = Task.Run(async () =>
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                try
                {
                    var now = DateTime.UtcNow;

                    // Use .ToArray() or .ToList() to take a safe snapshot
                    foreach (var kvp in _data.ToArray())
                    {
                        if (kvp.Value.ExpiryUtc.HasValue &&
                            kvp.Value.ExpiryUtc.Value <= now)
                        {
                            _data.TryRemove(kvp.Key, out _);
                        }
                    }
                }
                catch (Exception ex)
                {
                    // At minimum log to console for now
                    // Later: inject ILogger and use proper logging
                    Console.Error.WriteLine($"[ExpiryTask] Error during cleanup: {ex.Message}");
                    // Optional: add small back-off if you want
                    // await Task.Delay(5000, _cts.Token);
                }

                try
                {
                    await Task.Delay(1000, _cts.Token);
                }
                catch (OperationCanceledException)
                {
                    // Normal shutdown
                    break;
                }
            }
        }, _cts.Token);
    }

    private void LoadAof()
    {
        if (!File.Exists(_aofPath))
        {
            Debug.WriteLine("[AOF] No file found - starting fresh");
            return;
        }

        Debug.WriteLine($"[AOF] Loading from: {_aofPath}");

        try
        {
            using var reader = new StreamReader(_aofPath);
            string? line;
            int count = 0;

            while ((line = reader.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                count++;

                // Very simple parsing: assume format "SET key value" or "DEL key" or "EXPIRE key sec"
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2) continue;

                string cmd = parts[0].ToUpperInvariant();

                try
                {
                    switch (cmd)
                    {
                        case "SET":
                            if (parts.Length >= 3)
                                Set(parts[1], string.Join(" ", parts[2..])); // naive join, no quotes handling yet
                            break;

                        case "DEL":
                            if (parts.Length >= 2)
                                Del(parts[1]);
                            break;

                        case "EXPIRE":
                            if (parts.Length >= 3 && int.TryParse(parts[2], out int sec) && sec > 0)
                                Expire(parts[1], sec);
                            break;

                            // Add more commands later (FLUSHDB, etc.)
                    }
                }
                catch { /* skip broken lines */ }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AOF LOAD ERROR] {ex.Message}");
        }
        finally
        {
            Debug.WriteLine("AOF load finished.");
        }
    }

    public void AppendToAof(string commandLine)
    {
        lock (_aofLock)
        {
            if (_aofWriter == null)
            {
                Debug.WriteLine("[AOF CRITICAL] _aofWriter is null - file cannot be written!");
                return;
            }

            Debug.WriteLine($"[AOF APPEND] Writing: {commandLine}");
            _aofWriter.WriteLine(commandLine);
            _aofWriter.Flush();           // force write to disk now (good for debugging)
        }
    }

    // ---------------- Core KV ----------------
    public bool Set(string key, string value)
    {
        _data[key] = new ValueEntry { Value = value, ExpiryUtc = null };
        return true;
    }

    public string? Get(string key)
    {
        if (!_data.TryGetValue(key, out var entry)) return null;
        if (entry.ExpiryUtc.HasValue && entry.ExpiryUtc.Value <= DateTime.UtcNow)
        {
            _data.TryRemove(key, out _);
            return null;
        }

        return entry.Value;
    }

    public bool Del(string key) => _data.TryRemove(key, out _);

    public bool Exists(string key) => Get(key) != null;

    public void FlushAll() => _data.Clear();

    public bool Expire(string key, int seconds)
    {
        if (!_data.TryGetValue(key, out var entry)) return false;
        entry.ExpiryUtc = DateTime.UtcNow.AddSeconds(seconds);
        return true;
    }

    public long TTL(string key)
    {
        if (!_data.TryGetValue(key, out var entry)) return -2;
        return entry.GetTtlSeconds();
    }

    // ---------------- UI / DataGrid Support ----------------

    public IEnumerable<string> Keys
    {
        get
        {
            var now = DateTime.UtcNow;
            foreach (var kvp in _data)
            {
                if (kvp.Value.ExpiryUtc == null || kvp.Value.ExpiryUtc > now)
                    yield return kvp.Key;
            }
        }
    }


    public bool TryGetMeta(string key, out CacheMeta meta)
    {
        meta = default!;
        if (!_data.TryGetValue(key, out var entry)) return false;

        meta = new CacheMeta
        {
            Type = entry.Type,
            TtlSeconds = (int)entry.GetTtlSeconds(),
            SizeBytes = entry.Size
        };
        return true;
    }

    // Optional: Return key-value pairs (for GetAllKeys command)
    public IReadOnlyDictionary<string, string> GetAllKeys()
    {
        var now = DateTime.UtcNow;
        return _data
            .Where(kvp => kvp.Value.ExpiryUtc == null || kvp.Value.ExpiryUtc > now)
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Value, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<CacheItem> GetAllCacheItems()
    {
        var now = DateTime.UtcNow;
        return _data
            .Where(kvp => kvp.Value.ExpiryUtc == null || kvp.Value.ExpiryUtc > now)
            .Select(kvp => new CacheItem
            {
                Key = kvp.Key,
                Value = kvp.Value.Value,
                Type = kvp.Value.Type,
                TtlSeconds = (int)kvp.Value.GetTtlSeconds(),
                SizeBytes = kvp.Value.Size
            })
            .ToList()
            .AsReadOnly();
    }

    public void Dispose()
    {
        try
        {
            _cts.Cancel();
            _cts.Dispose();

            // Optional: wait for task to finish (with timeout)
            _expiryTask.Wait(TimeSpan.FromSeconds(2));
        }
        catch { /* ignore during dispose */ }

        lock (_aofLock)
        {
            if (_aofWriter != null)
            {
                try
                {
                    _aofWriter.Flush();
                    _aofWriter.Close();           // or Dispose()
                    _aofWriter = null;
                    Debug.WriteLine("[AOF] Closed writer successfully");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[AOF CLOSE ERROR] {ex.Message}");
                }
            }
        }
    }

}

public record CacheItem
{
    public string Key { get; init; } = "";
    public string Value { get; init; } = "";
    public string Type { get; init; } = "string";
    public int TtlSeconds { get; init; }
    public int SizeBytes { get; init; }
}
