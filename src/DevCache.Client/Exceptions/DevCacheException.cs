namespace DevCache.Client.Exceptions;

public class DevCacheException : Exception
{
    public DevCacheException(string message) : base(message) { }
}