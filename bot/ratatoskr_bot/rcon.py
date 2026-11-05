from __future__ import annotations

import asyncio
import itertools
import struct

AUTH = 3
EXEC = 2
AUTH_RESPONSE = 2


class RconError(Exception):
    pass


class Rcon:
    def __init__(self, host: str, port: int, password: str, timeout: float = 10.0):
        self.host, self.port, self.password, self.timeout = host, port, password, timeout
        self._reader: asyncio.StreamReader | None = None
        self._writer: asyncio.StreamWriter | None = None
        self._lock = asyncio.Lock()
        self._ids = itertools.count(1)

    async def _send(self, req_id: int, kind: int, body: str) -> None:
        data = body.encode() + b"\x00\x00"
        assert self._writer
        self._writer.write(struct.pack("<iii", len(data) + 8, req_id, kind) + data)
        await self._writer.drain()

    async def _recv(self) -> tuple[int, int, str]:
        assert self._reader
        (size,) = struct.unpack("<i", await self._reader.readexactly(4))
        payload = await self._reader.readexactly(size)
        req_id, kind = struct.unpack("<ii", payload[:8])
        return req_id, kind, payload[8:-2].decode(errors="replace")

    async def _connect(self) -> None:
        self._reader, self._writer = await asyncio.wait_for(
            asyncio.open_connection(self.host, self.port), self.timeout
        )
        await self._send(next(self._ids), AUTH, self.password)
        while True:
            req_id, kind, _ = await asyncio.wait_for(self._recv(), self.timeout)
            if kind == AUTH_RESPONSE:
                break
        if req_id == -1:
            await self.close()
            raise RconError("RCON authentication failed")

    async def close(self) -> None:
        if self._writer:
            self._writer.close()
            try:
                await self._writer.wait_closed()
            except OSError:
                pass
        self._reader = self._writer = None

    async def command(self, cmd: str) -> str:
        async with self._lock:
            for attempt in (1, 2):
                try:
                    if self._writer is None:
                        await self._connect()
                    req_id = next(self._ids)
                    await self._send(req_id, EXEC, cmd)
                    while True:
                        rid, _, body = await asyncio.wait_for(self._recv(), self.timeout)
                        if rid == req_id:
                            return body
                except (OSError, asyncio.IncompleteReadError, asyncio.TimeoutError) as e:
                    await self.close()
                    if attempt == 2:
                        raise RconError(f"RCON unreachable: {e!r}") from e
            raise AssertionError("unreachable")
