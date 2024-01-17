using System.Collections.Concurrent;

using BanSync.Database;
using BanSync.Utils;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;

using BanRequest = (ulong UserId, ulong SourceGuildId, ulong TargetGuildId, string? Reason);
using UnbanRequest = (ulong UserId, ulong SourceGuildId, ulong TargetGuildId, string? Reason);

namespace BanSync.Services;

public class BanSyncService : BackgroundService
{
    private readonly ConcurrentQueue<BanRequest> _banQueue;
    private readonly ConcurrentQueue<UnbanRequest> _unbanQueue;

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
        _unbanQueue = new();
        _client.AuditLogCreated += OnBanAuditLogCreated;
        _client.AuditLogCreated += OnUnbanAuditLogCreated;
    }

    private Task OnUnbanAuditLogCreated(SocketAuditLogEntry entry, SocketGuild guild)
    {
        if (entry.Data is not SocketUnbanAuditLogData data)
            return Task.CompletedTask;

        _ = Task.Run(async () =>
        {
            if (_banSyncState.UnbanHistory.All(x => x.UserId != data.Target.Id && x.SourceGuildId != guild.Id))
            {
                _banSyncState.UnbanHistory.Enqueue((guild.Id, data.Target.Id));
                if (_banSyncState.UnbanHistory.Count > 500)
                    _banSyncState.UnbanHistory.TryDequeue(out _);
            }
            else
                return;

            await using var db = await _dbContextFactory.CreateDbContextAsync();

            foreach (var g in _client.Guilds)
            {
                if (g.Id == guild.Id)
                    continue;

                var settings = await GuildSettingsUtils.GetGuildSettingsAsync(db, g.Id);
                if (settings.IsBanSyncEnabled || settings.UnbanNotificationsChannelId is not null)
                    _unbanQueue.Enqueue((data.Target.Id, guild.Id, g.Id, entry.Reason));
            }
        });

        return Task.CompletedTask;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        return Task.WhenAll(SyncBansAsync(stoppingToken), SyncUnbansAsync(stoppingToken));
    }

    private Task OnBanAuditLogCreated(SocketAuditLogEntry entry, SocketGuild guild)
    {
        if (entry.Data is not SocketBanAuditLogData data)
            return Task.CompletedTask;

        _ = Task.Run(async () =>
        {
            if (!_banSyncState.History.Any(x => x.UserId == data.Target.Id && x.SourceGuildId == guild.Id))
            {
                _banSyncState.History.Enqueue((guild.Id, data.Target.Id));
                if (_banSyncState.History.Count > 500)
                    _banSyncState.History.TryDequeue(out _);
            }
            else
                return;

            await using var db = await _dbContextFactory.CreateDbContextAsync();

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
                await using var db = await _dbContextFactory.CreateDbContextAsync(token);

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
                if (await db.BanExemptions.FirstOrDefaultAsync(x => x.UserId == ban.UserId && x.GuildId == ban.TargetGuildId) is null)
                {
                    var reason = $"Synced ban with {sourceGuild.Name}. | {ban.Reason}";
                    _logger.LogInformation(reason);
                    await guild.AddBanAsync(ban.UserId, 0, reason.Length > 512 ? reason[..512] : reason);
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error syncing ban with guild {Guild} ({Id})", sourceGuild.Name, sourceGuild.Id);
            }
        }

        if (settings.NotificationsChannelId is not null)
        {
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

                    await channel.SendMessageAsync(embed: embed, components: settings.IsBanSyncEnabled ? null : components);
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error sending ban notification in guild {Guild} ({Id})", guild.Name, guild.Id);
            }
        }
    }

    private async Task SyncUnbansAsync(CancellationToken token)
    {
        var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));

        while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
        {
            if (_unbanQueue.IsEmpty)
                continue;

            try
            {
                await using var db = await _dbContextFactory.CreateDbContextAsync(token);

                var tasks = new List<Task>(10);
                for (var i = 0; i < 10; i++)
                {
                    if (!_unbanQueue.TryDequeue(out var ban))
                        break;

                    tasks.Add(SyncUnbanWithGuildAsync(db, ban));
                }

                await Task.WhenAll(tasks).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error syncing unban");
            }
        }
    }

    public async Task SyncUnbanWithGuildAsync(AppDbContext db, UnbanRequest unban)
    {
        var guild = _client.GetGuild(unban.TargetGuildId);
        var sourceGuild = _client.GetGuild(unban.SourceGuildId);

        if (guild is null || sourceGuild is null)
            return;

        var settings = await GuildSettingsUtils.GetGuildSettingsAsync(db, guild.Id);

        if (settings.UnbanNotificationsChannelId is not null)
        {
            try
            {
                var channel = guild.GetTextChannel(settings.UnbanNotificationsChannelId.Value);

                if (channel is not null)
                {
                    var user = await _client.GetUserAsync(unban.UserId);

                    var embed = new EmbedBuilder()
                        .WithColor(0xFF8800)
                        .WithTitle("Unban Sync")
                        .WithDescription($"User <@{unban.UserId}> {(user is null ? string.Empty : $"`{user}`")} was unbanned in {sourceGuild.Name} ({sourceGuild.Id})")
                        .AddField("Reason", unban.Reason?.Length > 0 ? unban.Reason : "No reason")
                        .Build();

                    var components = new ComponentBuilder()
                        .WithButton("Unban", $"sync_unban_{unban.UserId}_{unban.SourceGuildId}", ButtonStyle.Success, Emoji.Parse(":white_check_mark:"))
                        .Build();

                    await channel.SendMessageAsync(embed: embed, components: components);
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error sending unban notification in guild {Guild} ({Id})", guild.Name, guild.Id);
            }
        }
    }

}