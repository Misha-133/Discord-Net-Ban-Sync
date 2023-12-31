﻿global using Discord;
global using Discord.Interactions;
global using Discord.WebSocket;

global using Microsoft.Extensions.Configuration;
global using Microsoft.Extensions.Logging;
using Discord.Net.BanSync.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Serilog;


var builder = new HostBuilder();

builder.ConfigureAppConfiguration(options
    => options.AddJsonFile("appsettings.json")
        .AddEnvironmentVariables("DNET_"));

var loggerConfig = new LoggerConfiguration()
    .WriteTo.Console()
    .WriteTo.File($"logs/log-{DateTime.Now:yy.MM.dd_HH.mm}.log")
    .CreateLogger();

builder.ConfigureServices((host, services) =>
{
    services.AddLogging(options => options.AddSerilog(loggerConfig, dispose: true));

    services.AddSingleton(new DiscordSocketClient(
        new DiscordSocketConfig
        {
            GatewayIntents = GatewayIntents.Guilds | GatewayIntents.GuildBans,
            FormatUsersInBidirectionalUnicode = false,
            AlwaysDownloadUsers = false,
            LogGatewayIntentWarnings = false,
            LogLevel = LogSeverity.Info
        }));

    services.AddSingleton(x => new InteractionService(x.GetRequiredService<DiscordSocketClient>(), new InteractionServiceConfig()
    {
        LogLevel = LogSeverity.Info
    }));

    services.AddSingleton<InteractionHandler>();

    services.AddHostedService<DiscordBotService>();
});

var app = builder.Build();

await app.RunAsync();