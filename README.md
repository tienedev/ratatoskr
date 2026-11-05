# Ratatoskr

Server-side Valheim plugin that exposes the world over RCON as JSON, plus a Discord bot built on it.

Players install nothing. A dedicated server keeps an up-to-date copy of every persistent object — chests, carts, ships, tombstones, portals, signs, build pieces — and Ratatoskr reads it.

## What you get

- Search every chest: where is the iron, how much stone is stored in total
- Chest journal: who took or added what, and when
- Tombstones with their position and contents
- Portals and unpaired tags
- Events: joins, leaves, deaths, raids (with the in-game message), boss kills, first player to reach a new biome
- Play time per player, build pieces per player
- Item names in any language shipped with the game

## Requirements

- Valheim dedicated server with BepInEx (tested on Valheim 1.0.21, BepInExPack 5.4.2360)
- [ValheimRcon](https://thunderstore.io/c/valheim/p/Tristan/ValheimRcon/) 1.6.3 or later

## Configuration

`BepInEx/config/dev.tiene.ratatoskr.cfg`:

```ini
[General]
Language = French
```

`Language` is any column of the game's localization files (English, French, German, …). Searches match that language, English and prefab names.

## Commands

Every command returns JSON. ValheimRcon truncates replies above about 4 KB, so lists are paged: `{"total", "offset", "next", "items": [...]}`. Repeat the call with `-offset <next>` until `next` is `null`.

| Command | Returns |
|---|---|
| `rtWorld` | day, time, current raid, global keys, players online |
| `rtPlayers` | online players with position, biome and health |
| `rtContainers [-near x z r] [-all]` | storage with slots used and a label taken from the nearest sign |
| `rtContainer <id or sign text>` | contents of one container |
| `rtFind <item> [-near x z r]` | containers holding an item, plus totals |
| `rtStock [item] [-near x z r]` | totals across all storage |
| `rtJournal [item or chest] [-player name] [-days n]` | chest changes, newest first |
| `rtTombs [player]` | tombstones: owner, position, in-game day of death, best items |
| `rtPortals` | portals with tag and biome |
| `rtBuilds [-near x z r]` | build pieces per player |
| `rtPlaytime [-days n]` | play time per player |
| `rtEvents [-since seq]` | events after a sequence number |
| `rtPrefabs [-near x z r]` | object counts by prefab |

`-all` includes containers nobody built, such as dungeon loot.

Sessions, chest journal, discovered biomes and player names are stored as TSV files next to the config.

### Notes

- Object IDs change when the server restarts.
- ValheimRcon logs every command result at `Message` level. On the `lloesche/valheim-server` image a large burst of output can block the log pipeline and freeze the game: remove `Message` from `[Logging.Console] LogLevels` in `BepInEx.cfg`.
- Character stats (damage, kills, skills, personal inventories) live on each player's machine and never reach the server, so they are out of reach for a server-side mod.

## Discord bot

`bot/` is a [discord.py](https://discordpy.readthedocs.io/) bot that talks to the plugin over RCON.

Slash commands: `/online`, `/monde`, `/cherche`, `/stock`, `/journal`, `/tombe`, `/portails`, `/classement`, `/tempsdejeu`, and `/admin` (save, announcement, kick, teleport to a player or a tombstone, give, stop raid). Events are posted to a channel and the bot status shows players online and the in-game day. Messages are in French.

```sh
pip install ./bot
RCON_HOST=… RCON_PASSWORD=… DISCORD_TOKEN=… DISCORD_GUILD_ID=… DISCORD_ALERT_CHANNEL_ID=… ratatoskr-bot
```

| Variable | |
|---|---|
| `RCON_HOST`, `RCON_PORT` | ValheimRcon address (port defaults to 2458) |
| `RCON_PASSWORD`, `DISCORD_TOKEN` | secrets; `*_FILE` variants read them from a file |
| `DISCORD_GUILD_ID` | registers commands on that server instantly |
| `DISCORD_ALERT_CHANNEL_ID` | channel for events |
| `DISCORD_ADMIN_IDS` | users allowed to run `/admin` (defaults to the application owner) |
| `CHEST_ALERTS=1` | also post chest changes |
| `POLL_SECONDS` | event polling interval (default 10) |

Add the bot's IP to ValheimRcon's whitelist. It reloads only on server restart.

## Building

Copy these assemblies from your server into `libs/`:

```
valheim_server_Data/Managed: assembly_valheim.dll assembly_utils.dll assembly_guiutils.dll
                             UnityEngine.dll UnityEngine.CoreModule.dll
BepInEx/core:                BepInEx.dll 0Harmony.dll
BepInEx/plugins:             ValheimRcon.dll
```

```sh
nix develop -c dotnet build -c Release   # or any .NET 8 SDK
```

Install `bin/Release/net48/Ratatoskr.dll` into `BepInEx/plugins/`.

## License

MIT
