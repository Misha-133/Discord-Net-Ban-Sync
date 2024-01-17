using System.Security.Cryptography.X509Certificates;
using BanSync.Database;
using BanSync.Services;
using BanSync.Utils;
using Microsoft.EntityFrameworkCore;

namespace BanSync.Modules;

[DefaultMemberPermissions(GuildPermission.Administrator)]
[Group("settings", "Settings command")]
public class CommandModule(ILogger<CommandModule> logger, IDbContextFactory<AppDbContext> dbContextFactory, BanSyncState syncState) : InteractionModuleBase<SocketInteractionContext>
{
	private AppDbContext db;

	public override async Task BeforeExecuteAsync(ICommandInfo command)
	{
		db = await dbContextFactory.CreateDbContextAsync();

		await base.BeforeExecuteAsync(command);
	}

	[SlashCommand("ban-enabled", "Sets whether bans will be synced with this guild")]
	public async Task BanEnabledAsync([Summary("is-enabled")] bool isEnabled)
	{
		await DeferAsync();
		var settings = await GuildSettingsUtils.GetGuildSettingsAsync(db, Context.Guild.Id);

		settings.IsBanSyncEnabled = isEnabled;
		await db.SaveChangesAsync();

		await FollowupAsync(embed: new EmbedBuilder()
			.WithColor(isEnabled ? 0xff00U : 0xff0000U)
			.WithDescription($"Ban sync in this guild is now `{(isEnabled ? "enabled" : "disabled")}`")
			.Build());
	}

	[SlashCommand("ban-notifications", "Sets the channel to post notifications to")]
	public async Task SetBanNotificationsChannelAsync([Summary("channel", "Sets the channel to post notifications to. Leave empty to disable"),
													ChannelTypes(ChannelType.Text, ChannelType.News)] IGuildChannel? channel = null)
	{
		await DeferAsync();
		var settings = await GuildSettingsUtils.GetGuildSettingsAsync(db, Context.Guild.Id);

		settings.NotificationsChannelId = channel?.Id;
		await db.SaveChangesAsync();

		if (channel is not null)
			await FollowupAsync(embed: new EmbedBuilder()
			.WithColor(0xff00U)
			.WithDescription($"Ban sync notifications channel is now set to <#{channel.Id}>")
			.Build());
		else
			await FollowupAsync(embed: new EmbedBuilder()
				.WithColor(0xff00U)
				.WithDescription($"Ban sync notifications channel are now disabled")
				.Build());
	}

	[SlashCommand("unban-notifications", "Sets the channel to post unban notifications to")]
	public async Task SetUnbanNotificationsChannelAsync([Summary("channel", "Sets the channel to post notifications to. Leave empty to disable"),
													ChannelTypes(ChannelType.Text, ChannelType.News)] IGuildChannel? channel = null)
	{
		await DeferAsync();
		var settings = await GuildSettingsUtils.GetGuildSettingsAsync(db, Context.Guild.Id);

		settings.UnbanNotificationsChannelId = channel?.Id;
		await db.SaveChangesAsync();

		if (channel is not null)
			await FollowupAsync(embed: new EmbedBuilder()
				.WithColor(0xff00U)
				.WithDescription($"Unban notifications channel is now set to <#{channel.Id}>")
				.Build());
		else
			await FollowupAsync(embed: new EmbedBuilder()
				.WithColor(0xff00U)
				.WithDescription($"Unban notifications are now disabled")
				.Build());
	}

    [SlashCommand("get-settings", "get settings for this guild")]
	public async Task GetGuildSettingsAsync()
	{
		await DeferAsync();
		var settings = await GuildSettingsUtils.GetGuildSettingsAsync(db, Context.Guild.Id);

		await FollowupAsync(embed: new EmbedBuilder()
					.WithColor(0xff00U)
					.AddField("Ban Sync", settings.IsBanSyncEnabled ? "`Enabled`" : "`Disabled`", true)
					.AddField("Notifications Channel", settings.NotificationsChannelId is null ? "`Disabled`" : $"<#{settings.NotificationsChannelId}>", true)
					.Build());
	}


	[Group("exemption", "exemption commands")]
	[DefaultMemberPermissions(GuildPermission.Administrator)]
	public class ExemptionCommands(ILogger<CommandModule> logger, IDbContextFactory<AppDbContext> dbContextFactory, BanSyncState syncState) : InteractionModuleBase<SocketInteractionContext>
	{
		private AppDbContext db;

		public override async Task BeforeExecuteAsync(ICommandInfo command)
		{
			db = await dbContextFactory.CreateDbContextAsync();

			await base.BeforeExecuteAsync(command);
		}

		[SlashCommand("add", "Exempt from being banned by ban sync")]
		public async Task ExemptUserAsync(IUser user)
		{
			await DeferAsync(true);

			if (await db.BanExemptions.FirstOrDefaultAsync(x => x.GuildId == Context.Guild.Id && x.UserId == user.Id) is not null)
			{
				await FollowupAsync("This user is already exempted");
				return;
			}

			await db.BanExemptions.AddAsync(new BanExemption
			{
				UserId = user.Id,
				GuildId = Context.Guild.Id
			});
			await db.SaveChangesAsync();
			await FollowupAsync($"User {user.Mention} is now exempted.");
		}

		[SlashCommand("remove", "Remove exemption from a user.")]
		public async Task RemoveExemptionUserAsync(IUser user)
		{
			await DeferAsync(true);

			var exemption = await db.BanExemptions.FirstOrDefaultAsync(x => x.GuildId == Context.Guild.Id && x.UserId == user.Id);

            if (exemption is null)
			{
				await FollowupAsync("This user is not exempted");
				return;
			}

			db.BanExemptions.Remove(exemption);
			await db.SaveChangesAsync();
			await FollowupAsync($"User {user.Mention} is not exempted anymore.");
		}
    }

    [RequireBotPermission(GuildPermission.BanMembers)]
	[RequireUserPermission(GuildPermission.BanMembers)]
	[ComponentInteraction($"sync_ban_*_*", true)]
	public async Task SyncBanAsync(ulong id, ulong guildId)
	{
		var interaction = (IComponentInteraction)Context.Interaction;
		await interaction.UpdateAsync(x => x.Components = null);

		var source = Context.Client.GetGuild(guildId);
		var reason = $"Synced ban with {source.Name}";

		if (interaction.Message.Embeds.Count is not 0)
		{
			var r = interaction.Message.Embeds.First().Fields.First(x => x.Name == "Reason");
			reason += $" | {r.Value}";
		}

		try
		{
			await Context.Guild.AddBanAsync(id, 0, reason.Length > 512 ? reason[..512] : reason);

			await FollowupAsync("User banned.", ephemeral: true);
		}
		catch (Exception ex)
		{
			await FollowupAsync("Failed to ban this user.", ephemeral: true);
			//logger.LogError(ex, "Failed to ban user {Id}", id);
		}
	}

	[RequireBotPermission(GuildPermission.BanMembers)]
	[RequireUserPermission(GuildPermission.BanMembers)]
	[ComponentInteraction($"sync_unban_*_*", true)]
	public async Task SyncUnBanAsync(ulong id, ulong guildId)
	{
		var interaction = (IComponentInteraction)Context.Interaction;
		await interaction.UpdateAsync(x => x.Components = null);

		var source = Context.Client.GetGuild(guildId);
		var reason = $"Synced unban with {source.Name}";

		if (interaction.Message.Embeds.Count is not 0)
		{
			var r = interaction.Message.Embeds.First().Fields.First(x => x.Name == "Reason");
			reason += $" | {r.Value}";
		}

		try
		{
			await Context.Guild.RemoveBanAsync(id, new RequestOptions
			{
				AuditLogReason = reason.Length > 512 ? reason[..512] : reason
            });

			await FollowupAsync("User unbanned.", ephemeral: true);
		}
		catch (Exception ex)
		{
			await FollowupAsync("Failed to unban this user.", ephemeral: true);
			//logger.LogError(ex, "Failed to ban user {Id}", id);
		}
	}
}