# Discord.Net Ban Sync
A discord bot for syncing bans

Run in docker
```bash
docker run -d -e DNET_Secrets__Discord='' \
-e DNET_ConnectionStrings__BanSync='' \
--name dnet_bans ghcr.io/misha-133/discord-net-ban-sync:master
```
