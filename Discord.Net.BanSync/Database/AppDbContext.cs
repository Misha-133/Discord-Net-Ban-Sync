using Microsoft.EntityFrameworkCore;

namespace BanSync.Database;

public class AppDbContext : DbContext
{
	public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
	{ }

	public DbSet<GuildSettings> GuildSettings { get; set; }

	public DbSet<BanExemption> BanExemptions { get; set; }
}