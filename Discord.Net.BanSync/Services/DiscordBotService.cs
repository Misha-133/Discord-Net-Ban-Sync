using System.Collections.Concurrent;

using Microsoft.Extensions.Hosting;

namespace Discord.Net.BanSync.Services;

public class DiscordBotService(DiscordSocketClient client, InteractionService interactions, IConfiguration config, ILogger<DiscordBotService> logger, InteractionHandler interactionHandler) : IHostedService
{
    private readonly ILogger _logger = logger;

    public static ConcurrentQueue<BanRequest> BanQueue = new();
    public ConcurrentDictionary<ulong, ConcurrentBag<(ulong, string?)>> BansPerGuild = new();

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        client.Ready += ClientReady;
        //client.UserBanned += OnClientUserBanned;
        client.AuditLogCreated += Client_AuditLogCreated;

        client.Log += LogAsync;
        interactions.Log += LogAsync;

        await interactionHandler.InitializeAsync();

        await client.LoginAsync(TokenType.Bot, config["Secrets:Discord"]);

        await client.StartAsync();
    }

    private Task Client_AuditLogCreated(SocketAuditLogEntry entry, SocketGuild guild)
    {
        if (guild.Id != 81384788765712384)
            return Task.CompletedTask;
        if (entry.Data is not SocketBanAuditLogData data)
            return Task.CompletedTask;

        _ = Task.Run(async () =>
        {
            foreach (var g in client.Guilds)
            {
                if (g.Id == guild.Id)
                    continue;

                var reason = $"Synced ban with DApi. | {entry.Reason}";

				var user = data.Target.Value?.ToString() ?? (await client.GetUserAsync(data.Target.Id))?.ToString() ?? "Not cached";

                await g.AddBanAsync(data.Target.Id, 7, reason);
                _logger.LogInformation("Synced ban with DApi. User: {User} ({Id}); Guild: {Guild}", user, data.Target.Id, g.Name);
            }
        });

        return Task.CompletedTask;

    }

    //private Task OnClientUserBanned(SocketUser user, SocketGuild guild)
    //{
    //    if (guild.Id == 81384788765712384)
    //    {
    //        _ = Task.Run(async () =>
    //        {
    //            foreach (var g in client.Guilds)
    //            {
    //                if (g.Id == guild.Id)
    //                    continue;

    //                await g.AddBanAsync(user.Id, 7, "Synced ban with DApi.");
    //                _logger.LogInformation("Synced ban with DApi. User: {User} ({Id}); Guild: {Guild}", user.ToString(), user.Id, g.Name);
    //            }
    //        });
    //    }

    //    return Task.CompletedTask;
    //}

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await client.StopAsync();
    }

    private async Task ClientReady()
    {
        _logger.LogInformation("Logged as {Bot}", client.CurrentUser);

        await interactions.RegisterCommandsGloballyAsync();
        await client.SetStatusAsync(UserStatus.Invisible);
    }

    public Task LogAsync(LogMessage msg)
    {
        var severity = msg.Severity switch
        {
            LogSeverity.Critical => LogLevel.Critical,
            LogSeverity.Error => LogLevel.Error,
            LogSeverity.Warning => LogLevel.Warning,
            LogSeverity.Info => LogLevel.Information,
            LogSeverity.Verbose => LogLevel.Trace,
            LogSeverity.Debug => LogLevel.Debug,
            _ => LogLevel.Information
        };

        _logger.Log(severity, msg.Exception, msg.Message);

        return Task.CompletedTask;
    }
}

public struct BanRequest(ulong userId, ulong guildId, string reason)
{
    public ulong UserId { get; set; } = userId;
    public ulong GuildId { get; set; } = guildId;
    public string Reason { get; set; } = reason;
}