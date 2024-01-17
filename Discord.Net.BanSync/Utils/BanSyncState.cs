using System.Collections.Concurrent;

namespace BanSync.Utils;

public class BanSyncState
{
	public readonly ConcurrentQueue<(ulong SourceGuildId, ulong UserId)> History = new();

	public readonly ConcurrentQueue<(ulong SourceGuildId, ulong UserId)> UnbanHistory = new();
}