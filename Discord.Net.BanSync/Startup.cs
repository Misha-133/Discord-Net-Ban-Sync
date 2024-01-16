global using Discord;
global using Discord.Interactions;
global using Discord.WebSocket;

global using Microsoft.Extensions.Configuration;
global using Microsoft.Extensions.Logging;
using BanSync.Database;
using BanSync.Services;
using BanSync.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Serilog;


var builder = new HostBuilder();

builder.ConfigureAppConfiguration(options
    => options.AddJsonFile("appsettings.json", true)
        .AddEnvironmentVariables("DNET_"));

var loggerConfig = new LoggerConfiguration()
    .WriteTo.Console()
    .WriteTo.File($"logs/log-{DateTime.Now:yy.MM.dd_HH.mm}.log")
	.MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Command", Serilog.Events.LogEventLevel.Warning)
    .CreateLogger();

builder.ConfigureServices((host, services) =>
{
    services.AddLogging(options => options
		.AddSerilog(loggerConfig, dispose: true));

	services.AddDbContextFactory<AppDbContext>(x => 
		x.UseMySql(host.Configuration.GetConnectionString("BanSync"), 
			ServerVersion.AutoDetect(host.Configuration.GetConnectionString("BanSync")),
			options => options.EnableRetryOnFailure(5, TimeSpan.FromSeconds(10), null)));

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
	services.AddSingleton<BanSyncState>();

    services.AddHostedService<DiscordBotService>();
	services.AddHostedService<BanSyncService>();
});

var app = builder.Build();

await app.RunAsync();