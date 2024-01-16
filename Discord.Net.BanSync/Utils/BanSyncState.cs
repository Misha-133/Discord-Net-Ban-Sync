using System.Collections.Concurrent;

namespace BanSync.Utils;

public class BanSyncState
{
	public readonly ConcurrentQueue<(ulong GuildId, ulong UserId)> History = new();
}