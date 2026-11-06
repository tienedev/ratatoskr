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

- Valheim dedicated server with [BepInExPack Valheim](https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/) (tested on Valheim 1.0.21, BepInExPack 5.4.2360)
- [ValheimRcon](https://thunderstore.io/c/valheim/p/Tristan/ValheimRcon/) 1.6.3 or later

Both are installed automatically when you use a mod manager.

## Install the plugin

Install on the **server** only.

**With a mod manager** ([r2modman](https://thunderstore.io/c/valheim/p/ebkr/r2modman/), Thunderstore Mod Manager, or your host's mod installer): search for **Ratatoskr** and install it. BepInEx and ValheimRcon come with it.

**By hand**:

1. Install [BepInExPack Valheim](https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/) on the server.
2. Download [ValheimRcon](https://thunderstore.io/c/valheim/p/Tristan/ValheimRcon/) and [Ratatoskr](https://thunderstore.io/c/valheim/p/tienedev/Ratatoskr/) (*Manual Download*).
3. Copy `ValheimRcon.dll` and `Ratatoskr.dll` from the zips' `plugins/` folders into `BepInEx/plugins/` on the server.
4. Start the server once so the config files are created, then stop it.

**Docker** ([lloesche/valheim-server](https://github.com/lloesche/valheim-server-docker)): set `BEPINEX=true`, put both DLLs in `/config/bepinex/plugins/`. Config files live in `/config/bepinex/`.

### Configure RCON

Edit `BepInEx/config/org.tristan.rcon.cfg`:

```ini
[1. Rcon]
Port = 2458
Password = choose-a-long-password
Whitelist IP mask = 127.0.0.1, <IP of the machine running the bot>
```

RCON does nothing while the password is empty. Restart the server after any change here: the whitelist is only read at startup.

Do not open the RCON port to the Internet. Run the bot on the same machine or network as the server.

### Check it works

From any RCON client (for example [rcon-cli](https://github.com/gorcon/rcon-cli)), on the server:

```sh
rcon -a 127.0.0.1:2458 -p choose-a-long-password rtWorld
```

You get a JSON line with the in-game day and the players online.

### Plugin options

`BepInEx/config/dev.tiene.ratatoskr.cfg`:

```ini
[General]
Language = French
```

`Language` is any column of the game's localization files (English, French, German, …). Searches match that language, English and prefab names.

## Discord bot

The bot turns the plugin into slash commands and posts world events in a channel. Messages are in French.

Slash commands: `/online`, `/monde`, `/cherche`, `/stock`, `/journal`, `/tombe`, `/portails`, `/classement`, `/tempsdejeu`, and `/admin` (save, announcement, kick, teleport to a player or a tombstone, give, stop raid). The bot status shows players online and the in-game day.

### 1. Create the Discord application

1. Open the [Discord Developer Portal](https://discord.com/developers/applications) → **New Application**.
2. **Bot** → **Reset Token** and copy the token. No privileged intent is needed.
3. **OAuth2** → **URL Generator**: tick `bot` and `applications.commands`, then the permissions *View Channels*, *Send Messages* and *Embed Links*. Open the generated URL and add the bot to your server.
4. In Discord, enable **Settings → Advanced → Developer Mode**, then right-click your server and the events channel → **Copy ID**.

### 2. Run the bot

**Docker** (no checkout needed):

```sh
docker build -t ratatoskr-bot https://github.com/tienedev/ratatoskr.git#main:bot

docker run -d --name ratatoskr-bot --restart unless-stopped \
  -e RCON_HOST=192.168.1.10 \
  -e RCON_PASSWORD=choose-a-long-password \
  -e DISCORD_TOKEN=your-bot-token \
  -e DISCORD_GUILD_ID=123456789012345678 \
  -e DISCORD_ALERT_CHANNEL_ID=123456789012345678 \
  ratatoskr-bot
```

If the bot runs on the game server itself with Docker, use `--network host` and `RCON_HOST=127.0.0.1`.

**Python** (3.11 or later):

```sh
pip install "git+https://github.com/tienedev/ratatoskr.git#subdirectory=bot"
RCON_HOST=… RCON_PASSWORD=… DISCORD_TOKEN=… DISCORD_GUILD_ID=… DISCORD_ALERT_CHANNEL_ID=… ratatoskr-bot
```

**Nix**: `nix run github:tienedev/ratatoskr#bot`, or use `packages.<system>.bot` in a NixOS service.

In Discord, type `/online`. The commands appear right away when `DISCORD_GUILD_ID` is set; without it, Discord can take up to an hour to show them.

### Settings

| Variable | |
|---|---|
| `RCON_HOST`, `RCON_PORT` | ValheimRcon address (defaults to `127.0.0.1:2458`) |
| `RCON_PASSWORD`, `DISCORD_TOKEN` | secrets; `RCON_PASSWORD_FILE` and `DISCORD_TOKEN_FILE` read them from a file instead |
| `DISCORD_GUILD_ID` | registers commands on that server instantly |
| `DISCORD_ALERT_CHANNEL_ID` | channel for events |
| `DISCORD_ADMIN_IDS` | comma-separated user IDs allowed to run `/admin` (defaults to the application owner) |
| `CHEST_ALERTS=1` | also post chest changes |
| `POLL_SECONDS` | event polling interval (default 10) |

### Troubleshooting

- **"serveur ne répond plus"**: the bot cannot reach RCON. Check the password, that the bot's IP is in `Whitelist IP mask`, and that the server was restarted after editing it.
- **Commands missing in Discord**: set `DISCORD_GUILD_ID`, and check the invite URL included `applications.commands`.
- **Game freezes on `lloesche/valheim-server`**: ValheimRcon logs every command result at `Message` level, and a large burst can block the image's log pipeline. Remove `Message` from `[Logging.Console] LogLevels` in `BepInEx/config/BepInEx.cfg`.

## RCON commands

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

### Limits

- Object IDs change when the server restarts.
- Character stats (damage, kills, skills, personal inventories) live on each player's machine and never reach the server, so they are out of reach for a server-side mod.

## Building from source

Copy these assemblies from your server into `libs/` (they are not redistributable):

```
valheim_server_Data/Managed: assembly_valheim.dll assembly_utils.dll assembly_guiutils.dll
                             UnityEngine.dll UnityEngine.CoreModule.dll
BepInEx/core:                BepInEx.dll 0Harmony.dll
BepInEx/plugins:             ValheimRcon.dll
```

Then, with the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (or `nix develop`):

```sh
dotnet build -c Release
```

The plugin is `bin/Release/net48/Ratatoskr.dll`.

## License

MIT
