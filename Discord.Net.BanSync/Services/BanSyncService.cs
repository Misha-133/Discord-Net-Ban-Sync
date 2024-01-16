using System.Collections.Concurrent;

using BanSync.Database;
using BanSync.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;

using BanRequest = (ulong UserId, ulong SourceGuildId, ulong TargetGuildId, string? Reason);

namespace BanSync.Services;

public class BanSyncService : BackgroundService
{
    private readonly ConcurrentQueue<BanRequest> _banQueue;
    private readonly DiscordSocketClient _client;
    private readonly ILogger<BanSyncService> _logger;
	private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
    private readonly BanSyncState _banSyncState;
    
    public BanSyncService(DiscordSocketClient client, IDbContextFactory<AppDbContext> dbContextFactory, ILogger<BanSyncService> logger, BanSyncState state)
    {
        _client = client;
        _logger = logger;
        _dbContextFactory = dbContextFactory;
        _banSyncState = state;
        _banQueue = new();
        _client.AuditLogCreated += OnAuditLogCreated;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        return SyncBansAsync(stoppingToken);
    }

    private Task OnAuditLogCreated(SocketAuditLogEntry entry, SocketGuild guild)
    {
        if (entry.Data is not SocketBanAuditLogData data)
            return Task.CompletedTask;

        _ = Task.Run(async () =>
		{
			var db = await _dbContextFactory.CreateDbContextAsync();

            foreach (var g in _client.Guilds)
			{
                if (g.Id == guild.Id)
                    continue;

				var settings = await GuildSettingsUtils.GetGuildSettingsAsync(db, g.Id);
                if (settings.IsBanSyncEnabled || settings.NotificationsChannelId is not null)
                    _banQueue.Enqueue((data.Target.Id, guild.Id, g.Id, entry.Reason));
			}
        });

        return Task.CompletedTask;
    }

    private async Task SyncBansAsync(CancellationToken token)
    {
        var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));

        while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
        {
            if (_banQueue.IsEmpty)
                continue;

            try
			{
				var db = await _dbContextFactory.CreateDbContextAsync(token);

                var tasks = new List<Task>(10);
                for (var i = 0; i < 10; i++)
                {
                    if (!_banQueue.TryDequeue(out var ban))
                        break;

                    tasks.Add(SyncBanWithGuildAsync(db, ban));
                }

                await Task.WhenAll(tasks).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error syncing ban");
            }
        }
    }

	public async Task SyncBanWithGuildAsync(AppDbContext db, BanRequest ban)
	{
		var guild = _client.GetGuild(ban.TargetGuildId);
		var sourceGuild = _client.GetGuild(ban.SourceGuildId);

		if (guild is null || sourceGuild is null)
            return;

        var settings = await GuildSettingsUtils.GetGuildSettingsAsync(db, guild.Id);

		if (settings.IsBanSyncEnabled)
		{
			try
			{
				var reason = $"Synced ban with {guild.Name}. | {ban.Reason}";
                await guild.AddBanAsync(ban.UserId, 0, reason.Length > 512 ? reason[..512] : reason);
			}
			catch (Exception e)
			{
				_logger.LogError(e, "Error syncing ban with guild {Guild} ({Id})", guild.Name, guild.Id);
			}
		}

		if (settings.NotificationsChannelId is not null && _banSyncState.History.All(x => x.UserId != ban.UserId && x.GuildId != ban.TargetGuildId))
		{
			_banSyncState.History.Enqueue((ban.TargetGuildId, ban.UserId));
			if (_banSyncState.History.Count > 500)
				_banSyncState.History.TryDequeue(out _);
            try

			{
				var channel = guild.GetTextChannel(settings.NotificationsChannelId.Value);

				if (channel is not null)
				{
					var user = await _client.GetUserAsync(ban.UserId);

                    var embed = new EmbedBuilder()
						.WithColor(0xFF8800)
						.WithTitle("Ban Sync")
						.WithDescription($"User <@{ban.UserId}> {(user is null ? string.Empty : $"`{user}`")} was banned in {sourceGuild.Name} ({sourceGuild.Id})")
						.AddField("Reason", ban.Reason?.Length > 0 ? ban.Reason : "No reason")
						.Build();

					var components = new ComponentBuilder()
						.WithButton("Ban", $"sync_ban_{ban.UserId}_{ban.SourceGuildId}", ButtonStyle.Danger, Emote.Parse("<:banhammer:513640748801982496>"))
						.Build();

					await channel.SendMessageAsync(embed: embed, components: components);
				}
			}
			catch (Exception e)
			{
				_logger.LogError(e, "Error sending ban notification in guild {Guild} ({Id})", guild.Name, guild.Id);
			}
		}
    }
}