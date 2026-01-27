namespace DevCache.Client;

public interface IDevCacheClient : IDisposable
{
    // Strings
    Task<bool> SetAsync(string key, string value);
    Task<string?> GetAsync(string key);
    Task<long> IncrAsync(string key, long increment = 1);
    Task<long> DecrAsync(string key, long decrement = 1);
    Task<bool> DelAsync(string key);
    Task<bool> ExistsAsync(string key);
    Task<bool> ExpireAsync(string key, TimeSpan ttl);
    Task<long> TTLAsync(string key);

    // Lists
    Task<long> LPushAsync(string key, params string[] values);
    Task<long> RPushAsync(string key, params string[] values);
    Task<string?> LPopAsync(string key);
    Task<string?> RPopAsync(string key);
    Task<long> LLenAsync(string key);

    // Hashes
    Task<int> HSetAsync(string key, params string[] fieldValuePairs);
    Task<string?> HGetAsync(string key, string field);
    Task<bool> HDelAsync(string key, string field);
    Task<long> HLenAsync(string key);
    Task<IEnumerable<string>> HKeysAsync(string key);
    Task<IEnumerable<string>> HValsAsync(string key);

    // DB Operations
    Task<bool> FlushDbAsync();
    Task<bool> FlushAllAsync();
    Task<IEnumerable<string>> KeysAsync();
}