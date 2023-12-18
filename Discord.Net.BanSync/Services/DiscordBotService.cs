using Microsoft.Extensions.Hosting;

namespace Discord.Net.BanSync.Services;

public class DiscordBotService : IHostedService
{
    private readonly DiscordSocketClient _client;
    private readonly InteractionService _interactions;
    private readonly IConfiguration _config;
    private readonly ILogger _logger;
    private readonly InteractionHandler _interactionHandler;

    public DiscordBotService(DiscordSocketClient client, InteractionService interactions, IConfiguration config, ILogger<DiscordBotService> logger, InteractionHandler interactionHandler)
    {
        _client = client;
        _interactions = interactions;
        _config = config;
        _logger = logger;
        _interactionHandler = interactionHandler;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _client.Ready += ClientReady;
        _client.UserBanned += _client_UserBanned;

        _client.Log += LogAsync;
        _interactions.Log += LogAsync;

        await _interactionHandler.InitializeAsync();

        await _client.LoginAsync(TokenType.Bot, _config["Secrets:Discord"]);

        await _client.StartAsync();
    }

    private async Task _client_UserBanned(SocketUser user, SocketGuild guild)
    {
        if (guild.Id == 81384788765712384)
        {
            _ = Task.Run(async () =>
            {
                foreach (var g in _client.Guilds)
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
        await _client.StopAsync();
    }

    private async Task ClientReady()
    {
        _logger.LogInformation($"Logged as {_client.CurrentUser}");

        await _interactions.RegisterCommandsGloballyAsync();
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