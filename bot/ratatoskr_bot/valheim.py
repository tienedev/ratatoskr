from __future__ import annotations

import json
from typing import Any

from .rcon import Rcon, RconError


class Valheim:
    def __init__(self, rcon: Rcon):
        self.rcon = rcon

    @staticmethod
    def _arg(value: Any) -> str:
        # ValheimRcon groups double-quoted words and has no escaping.
        s = str(value).replace('"', "").strip()
        return f'"{s}"' if " " in s else s

    async def call(self, cmd: str, *args: Any) -> dict:
        line = " ".join([cmd, *(self._arg(a) for a in args)])
        raw = await self.rcon.command(line)
        try:
            data = json.loads(raw)
        except json.JSONDecodeError as e:
            raise RconError(f"unexpected reply to {cmd}: {raw[:200]}") from e
        if isinstance(data, dict) and "error" in data:
            raise RconError(data["error"])
        return data

    async def all(self, cmd: str, *args: Any, limit: int = 2000) -> tuple[list[dict], dict]:
        first = page = await self.call(cmd, *args)
        items = list(page["items"])
        while page.get("next") is not None and len(items) < limit:
            page = await self.call(cmd, *args, "-offset", page["next"])
            items.extend(page["items"])
        return items, first

    async def world(self) -> dict:
        return await self.call("rtWorld")

    async def players(self) -> list[dict]:
        return (await self.all("rtPlayers"))[0]

    async def find(self, query: str) -> tuple[list[dict], dict]:
        return await self.all("rtFind", query)

    async def stock(self, query: str = "") -> list[dict]:
        return (await self.all("rtStock", *([query] if query else [])))[0]

    async def tombs(self, player: str = "") -> list[dict]:
        return (await self.all("rtTombs", *([player] if player else [])))[0]

    async def portals(self) -> list[dict]:
        return (await self.all("rtPortals"))[0]

    async def builds(self) -> list[dict]:
        return (await self.all("rtBuilds"))[0]

    async def events(self, since: int) -> tuple[list[dict], str, int]:
        page = await self.call("rtEvents", "-since", since)
        boot, items = page["boot"], list(page["items"])
        while page.get("next") is not None and items:
            page = await self.call("rtEvents", "-since", items[-1]["seq"])
            items.extend(page["items"])
        return items, boot, page["seq"]

    async def journal(self, query: str = "", player: str = "", days: float = 0) -> list[dict]:
        args = [*([query] if query else []), *(["-player", player] if player else []),
                *(["-days", days] if days else [])]
        return (await self.all("rtJournal", *args, limit=200))[0]

    async def playtime(self, days: float = 0) -> list[dict]:
        return (await self.all("rtPlaytime", *(["-days", days] if days else [])))[0]
