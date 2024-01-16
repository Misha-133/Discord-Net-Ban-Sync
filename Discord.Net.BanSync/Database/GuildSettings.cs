using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BanSync.Database;

[Table("GuildSettings")]
public class GuildSettings
{
	[Key]
	[Column("Id")]
	public ulong GuildId { get; set; }

	[Column("BanSyncEnabled")]
	public bool IsBanSyncEnabled { get; set; }
	
	[Column("NotificationsChannelId")]
	public ulong? NotificationsChannelId { get; set; }
}