using System.Collections.Concurrent;

using Microsoft.Extensions.Hosting;

namespace BanSync.Services;

public class DiscordBotService(DiscordSocketClient client, InteractionService interactions, IConfiguration config, ILogger<DiscordBotService> logger, 
	InteractionHandler interactionHandler) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        client.Ready += ClientReady;

        client.Log += LogAsync;
        interactions.Log += LogAsync;

        await interactionHandler.InitializeAsync();

        await client.LoginAsync(TokenType.Bot, config["Secrets:Discord"]);

        await client.StartAsync();
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await client.StopAsync();
    }

    private async Task ClientReady()
    {
        logger.LogInformation("Logged in as {Bot}", client.CurrentUser);

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

        logger.Log(severity, msg.Exception, msg.Message);

        return Task.CompletedTask;
    }
}

