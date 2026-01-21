namespace DevCache.Core;

public delegate Task RedisCommand(CommandContext context, IReadOnlyList<string> args);