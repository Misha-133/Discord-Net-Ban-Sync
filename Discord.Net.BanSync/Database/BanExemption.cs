using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BanSync.Database;

[Table("BanExemptions")]
public class BanExemption
{
	[Key]
	[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
	public int Id { get; set; }

	[Required]
	public ulong UserId { get; set; }

	[Required]
	public ulong GuildId { get; set; }
}