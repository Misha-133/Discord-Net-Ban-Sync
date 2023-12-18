using Microsoft.Extensions.Hosting;

namespace Discord.Net.BanSync.Services;

public class DiscordBotService(DiscordSocketClient client, InteractionService interactions, IConfiguration config, ILogger<DiscordBotService> logger, InteractionHandler interactionHandler) : IHostedService
{
    private readonly ILogger _logger = logger;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        client.Ready += ClientReady;
        client.UserBanned += OnClientUserBanned;

        client.Log += LogAsync;
        interactions.Log += LogAsync;

        await interactionHandler.InitializeAsync();

        await client.LoginAsync(TokenType.Bot, config["Secrets:Discord"]);

        await client.StartAsync();
    }

    private async Task OnClientUserBanned(SocketUser user, SocketGuild guild)
    {
        if (guild.Id == 81384788765712384)
        {
            _ = Task.Run(async () =>
            {
                foreach (var g in client.Guilds)
                {
                    if (g.Id == guild.Id)
                        continue;

                    await g.AddBanAsync(user.Id, 7, "Synced ban with DApi.");
                    _logger.LogInformation("Synced ban with DApi. User: {user} ({id})", user.ToString(), user.Id);
                }
            });
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await client.StopAsync();
    }

    private async Task ClientReady()
    {
        _logger.LogInformation($"Logged as {client.CurrentUser}");

        await interactions.RegisterCommandsGloballyAsync();
    }

    public async Task LogAsync(LogMessage msg)
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

        await Task.CompletedTask;
    }
}