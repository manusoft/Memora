namespace DevCache.Cli.Handlers;

internal class DevCacheAutoCompleteHandler : IAutoCompleteHandler
{
    // Characters that separate words/tokens for completion
    // Common for Redis-style commands: space is the main separator
    public char[] Separators { get; set; } = new char[] { ' ' };

    // This method is called when user presses Tab
    // text     = full current input line
    // index    = cursor position in the line
    public string[]? GetSuggestions(string text, int index)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        // Split the current line into tokens
        var tokens = text.Split(Separators, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
            return null;

        string currentToken = tokens.Last(); // the word being typed right now
        string commandPrefix = tokens[0].ToUpperInvariant(); // first word = command

        // Case 1: User is typing the command itself (first word)
        if (tokens.Length == 1 && !text.EndsWith(" "))
        {
            var possibleCommands = GetAllSupportedCommands()
                .Where(c => c.StartsWith(currentToken, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (possibleCommands.Length > 0)
                return possibleCommands;
        }

        // Case 2: User is typing arguments after a known command
        // Provide context-aware suggestions where it makes sense
        switch (commandPrefix)
        {
            case "SET":
            case "GET":
            case "DEL":
            case "EXISTS":
            case "EXPIRE":
            case "TTL":
            case "GETMETA":
                // Suggest existing keys (you can fetch them dynamically if you want)
                // For now: use a static list or recent keys from history
                var possibleKeys = GetKnownKeys()
                    .Where(k => k.StartsWith(currentToken, StringComparison.OrdinalIgnoreCase))
                    .ToArray();

                if (possibleKeys.Length > 0)
                    return possibleKeys;
                break;

            case "KEYS":
                // Common patterns
                if (currentToken == "" || currentToken == "*")
                    return new[] { "*" };
                break;

            case "PING":
            case "ECHO":
                // Could suggest example messages, but usually not needed
                break;
        }

        return null; // no suggestions
    }

    // Helper: list of all commands your server supports
    private static IEnumerable<string> GetAllSupportedCommands()
    {
        yield return "PING";
        yield return "ECHO";
        yield return "SET";
        yield return "GET";
        yield return "DEL";
        yield return "EXISTS";
        yield return "EXPIRE";
        yield return "TTL";
        yield return "KEYS";
        yield return "FLUSHDB";
        yield return "GETMETA";
        // Add more as you implement new commands later
    }

    // Helper: suggest some known keys
    // In a real app you could cache recent keys or even query the server,
    // but for simplicity we'll use a small static list + any keys typed recently
    private static IEnumerable<string> GetKnownKeys()
    {
        // Static examples — you can expand this
        yield return "mykey";
        yield return "hello";
        yield return "counter";
        yield return "user:123";
        yield return "mycity";
        yield return "citykey";
        yield return "temp";

        // Bonus: you could collect keys from previous successful KEYS * responses
        // (would require storing them in a static list or field)
    }
}