using BanSync.Database;

namespace BanSync.Utils;

public class GuildSettingsUtils
{
    public static async Task<GuildSettings> GetGuildSettingsAsync(AppDbContext context, ulong guildId)
    {
        var settings = context.GuildSettings.FirstOrDefault(x => x.GuildId == guildId);
		if (settings is not null) 
			return settings;

		settings = new GuildSettings
		{
			GuildId = guildId,
			IsBanSyncEnabled = false,
			NotificationsChannelId = null
		};
		context.GuildSettings.Add(settings);
		await context.SaveChangesAsync();

		return settings;
    }
}
