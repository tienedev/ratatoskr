import logging
import os
import sys

from .bot import RatatoskrBot
from .rcon import Rcon
from .valheim import Valheim


def env(name: str, default: str | None = None) -> str:
    value = os.environ.get(name, default)
    if value is None:
        sys.exit(f"missing environment variable {name}")
    return value


def secret(name: str) -> str:
    path = os.environ.get(f"{name}_FILE")
    if path:
        with open(path) as f:
            return f.read().strip()
    return env(name)


def main() -> None:
    logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s %(name)s: %(message)s")
    rcon = Rcon(env("RCON_HOST", "127.0.0.1"), int(env("RCON_PORT", "2458")), secret("RCON_PASSWORD"))
    guild = os.environ.get("DISCORD_GUILD_ID")
    channel = os.environ.get("DISCORD_ALERT_CHANNEL_ID")
    bot = RatatoskrBot(
        Valheim(rcon),
        guild_id=int(guild) if guild else None,
        alert_channel_id=int(channel) if channel else None,
        admin_ids={int(i) for i in env("DISCORD_ADMIN_IDS", "").split(",") if i.strip()},
        poll_seconds=float(env("POLL_SECONDS", "10")),
        chest_alerts=env("CHEST_ALERTS", "0") == "1",
    )
    bot.run(secret("DISCORD_TOKEN"), log_handler=None)


if __name__ == "__main__":
    main()
