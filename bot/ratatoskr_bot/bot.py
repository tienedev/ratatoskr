from __future__ import annotations

import asyncio
import logging
import time

import discord
from discord import app_commands

from .rcon import RconError
from .valheim import Valheim

log = logging.getLogger("ratatoskr")

BIOMES = {
    "Meadows": "Prairie", "BlackForest": "Forêt noire", "Swamp": "Marais",
    "Mountain": "Montagnes", "Plains": "Plaines", "Mistlands": "Brumes",
    "AshLands": "Terres cendrées", "DeepNorth": "Grand Nord", "Ocean": "Océan",
}
BOSSES = [
    ("defeated_eikthyr", "Eikthyr"), ("defeated_gdking", "L'Ancien"),
    ("defeated_bonemass", "Bonemass"), ("defeated_dragon", "Moder"),
    ("defeated_goblinking", "Yagluth"), ("defeated_queen", "La Reine"),
    ("defeated_fader", "Fader"),
]
COLOR = 0x6B8E23


def biome(name: str | None) -> str:
    return BIOMES.get(name or "", name or "?")


def where(pos: list[float], b: str | None = None) -> str:
    s = f"x {pos[0]:.0f}, z {pos[2]:.0f}"
    return f"{biome(b)} ({s})" if b else s


def boss_name(key: str) -> str:
    for k, name in BOSSES:
        if k == key:
            return name
    return key.removeprefix("defeated_").capitalize()


def chunks(lines: list[str], limit: int = 3900) -> str:
    out, size = [], 0
    for i, line in enumerate(lines):
        if size + len(line) + 1 > limit:
            out.append(f"… et {len(lines) - i} de plus")
            break
        out.append(line)
        size += len(line) + 1
    return "\n".join(out) or "—"


class RatatoskrBot(discord.Client):
    def __init__(self, valheim: Valheim, guild_id: int | None, alert_channel_id: int | None,
                 admin_ids: set[int], poll_seconds: float = 10.0, chest_alerts: bool = False):
        super().__init__(intents=discord.Intents.default())
        self.tree = app_commands.CommandTree(self)
        self.vh = valheim
        self.guild = discord.Object(id=guild_id) if guild_id else None
        self.alert_channel_id = alert_channel_id
        self.admin_ids = admin_ids
        self.poll_seconds = poll_seconds
        self.chest_alerts = chest_alerts
        self._stock_cache: tuple[float, list[dict]] = (0.0, [])
        register(self)
        register_admin(self)

    async def setup_hook(self) -> None:
        if self.guild:
            self.tree.copy_global_to(guild=self.guild)
            await self.tree.sync(guild=self.guild)
        else:
            await self.tree.sync()
        if self.alert_channel_id:
            self.loop.create_task(Watcher(self).run())

    async def stock_items(self) -> list[dict]:
        at, items = self._stock_cache
        if time.monotonic() - at > 60:
            items = await self.vh.stock()
            self._stock_cache = (time.monotonic(), items)
        return items


def embed(title: str, description: str = "") -> discord.Embed:
    return discord.Embed(title=title, description=description, color=COLOR)


def register(bot: RatatoskrBot) -> None:
    tree = bot.tree

    async def fail(inter: discord.Interaction, e: Exception) -> None:
        msg = f"⚠️ Serveur injoignable ou erreur : `{e}`"
        if inter.response.is_done():
            await inter.followup.send(msg, ephemeral=True)
        else:
            await inter.response.send_message(msg, ephemeral=True)

    @tree.command(name="online", description="Qui est connecté sur le serveur Valheim ?")
    async def online(inter: discord.Interaction):
        try:
            players = await bot.vh.players()
        except RconError as e:
            return await fail(inter, e)
        if not players:
            return await inter.response.send_message(embed=embed("🌙 Personne en ligne"))
        lines = [
            f"**{p['name']}** — {where(p['pos'], p.get('biome'))}"
            f" · ❤️ {p.get('health', 0):.0f}{' · 💀' if p.get('dead') else ''}"
            for p in players
        ]
        await inter.response.send_message(embed=embed(f"🟢 {len(players)} en ligne", "\n".join(lines)))

    @tree.command(name="monde", description="Jour, heure, raid en cours et boss vaincus")
    async def monde(inter: discord.Interaction):
        try:
            w = await bot.vh.world()
        except RconError as e:
            return await fail(inter, e)
        keys = set(w.get("globalKeys", []))
        known = {k for k, _ in BOSSES}
        bosses = " ".join(f"{'✅' if k in keys else '❌'} {n}" for k, n in BOSSES)
        extra = sorted(k for k in keys if k.startswith("defeated_") and k not in known)
        if extra:
            bosses += " " + " ".join(f"✅ {boss_name(k)}" for k in extra)
        e = embed(f"🌍 Jour {w.get('day')} — {w.get('time')}")
        e.add_field(name="Raid", value=w["raid"] or "aucun", inline=True)
        e.add_field(name="Joueurs", value=str(w.get("players", 0)), inline=True)
        e.add_field(name="Boss", value=bosses, inline=False)
        await inter.response.send_message(embed=e)

    async def item_autocomplete(inter: discord.Interaction, current: str):
        try:
            items = await bot.stock_items()
        except RconError:
            return []
        cur = current.lower()
        names = [i["name"] for i in items if cur in i["name"].lower() or cur in i["item"].lower()]
        return [app_commands.Choice(name=n, value=n) for n in names[:25]]

    @tree.command(name="cherche", description="Dans quels coffres se trouve un objet ?")
    @app_commands.describe(objet="Nom de l'objet (ex. Iron, Wood, Coins)")
    @app_commands.autocomplete(objet=item_autocomplete)
    async def cherche(inter: discord.Interaction, objet: str):
        await inter.response.defer()
        try:
            hits, first = await bot.vh.find(objet)
        except RconError as e:
            return await fail(inter, e)
        if not hits:
            return await inter.followup.send(embed=embed(f"🔍 « {objet} »", "Introuvable dans les coffres."))
        lines = []
        for h in hits:
            what = ", ".join(f"{i['name']} ×{i['count']}" for i in h["items"])
            label = f"**{h['label']}**" if h.get("label") else h["prefab"].replace("piece_", "")
            lines.append(f"{label} · {where(h['pos'])} — {what}")
        totals = " · ".join(f"{k} ×{v}" for k, v in first.get("totals", {}).items())
        await inter.followup.send(embed=embed(f"🔍 « {objet} » — {totals}", chunks(lines)))

    @tree.command(name="stock", description="Inventaire total des coffres (filtrable)")
    @app_commands.describe(filtre="Filtrer par nom (optionnel)")
    async def stock(inter: discord.Interaction, filtre: str = ""):
        await inter.response.defer()
        try:
            items = await bot.vh.stock(filtre)
        except RconError as e:
            return await fail(inter, e)
        lines = [f"{i['name']} **×{i['count']}**" + (f" ({i['containers']} coffres)" if i["containers"] > 1 else "")
                 for i in items]
        await inter.followup.send(embed=embed(f"📦 Stock{f' « {filtre} »' if filtre else ''} — {len(items)} objets", chunks(lines)))

    @tree.command(name="tombe", description="Où sont les tombes et ce qu'elles contiennent")
    @app_commands.describe(joueur="Nom du joueur (optionnel)")
    async def tombe(inter: discord.Interaction, joueur: str = ""):
        try:
            tombs = await bot.vh.tombs(joueur)
        except RconError as e:
            return await fail(inter, e)
        if not tombs:
            return await inter.response.send_message(embed=embed("🪦 Aucune tombe", "Tout le monde a récupéré ses affaires."))
        lines = [
            f"**{t['owner']}** · jour {t.get('diedDay', '?')} · {where(t['pos'], t.get('biome'))}\n"
            f"  {t['stacks']} piles : {', '.join(t.get('top', []))}"
            for t in tombs
        ]
        await inter.response.send_message(embed=embed(f"🪦 {len(tombs)} tombe(s)", chunks(lines)))

    @tree.command(name="portails", description="Liste des portails et de leurs tags")
    async def portails(inter: discord.Interaction):
        try:
            portals = await bot.vh.portals()
        except RconError as e:
            return await fail(inter, e)
        lines = [f"{'🔗' if p['paired'] else '⚠️'} **{p['tag'] or '(sans tag)'}** — {where(p['pos'], p.get('biome'))}"
                 for p in portals]
        await inter.response.send_message(embed=embed(f"🌀 {len(portals)} portails", chunks(lines)))

    @tree.command(name="journal", description="Qui a pris ou déposé quoi dans les coffres")
    @app_commands.describe(recherche="Objet ou nom de coffre (optionnel)", joueur="Joueur (optionnel)",
                           jours="Sur combien de jours (défaut 7)")
    async def journal(inter: discord.Interaction, recherche: str = "", joueur: str = "",
                      jours: app_commands.Range[int, 1, 365] = 7):
        await inter.response.defer()
        try:
            entries = await bot.vh.journal(recherche, joueur, jours)
        except RconError as e:
            return await fail(inter, e)
        if not entries:
            return await inter.followup.send(embed=embed("📒 Journal des coffres", "Rien sur la période."))
        lines = []
        for e in entries:
            sign = "➕" if e["delta"] > 0 else "➖"
            chest = e.get("label") or e["container"].replace("piece_", "")
            lines.append(f"<t:{e['t'] // 1000}:R> {sign} **{e['player']}** {abs(e['delta'])} {e['name']} · {chest}")
        title = "📒 Journal des coffres" + (f" « {recherche} »" if recherche else "") + (f" — {joueur}" if joueur else "")
        await inter.followup.send(embed=embed(title, chunks(lines)))

    @tree.command(name="tempsdejeu", description="Qui a le plus joué ?")
    @app_commands.describe(jours="Sur combien de jours (vide = depuis le début)")
    async def tempsdejeu(inter: discord.Interaction, jours: app_commands.Range[int, 1, 365] | None = None):
        try:
            rows = await bot.vh.playtime(jours or 0)
        except RconError as e:
            return await fail(inter, e)
        medals = ["🥇", "🥈", "🥉"]
        lines = [f"{medals[i] if i < 3 else '▫️'} **{r['name']}** — {duration(r['seconds'])} "
                 f"({r['sessions']} session{'s' if r['sessions'] > 1 else ''}, vu <t:{r['lastSeen'] // 1000}:R>)"
                 for i, r in enumerate(rows)]
        period = f"{jours} derniers jours" if jours else "depuis l'installation"
        await inter.response.send_message(embed=embed(f"⏱️ Temps de jeu — {period}", chunks(lines) if lines else "Personne encore."))

    @tree.command(name="classement", description="Qui a le plus construit ?")
    async def classement(inter: discord.Interaction):
        try:
            builds = await bot.vh.builds()
        except RconError as e:
            return await fail(inter, e)
        medals = ["🥇", "🥈", "🥉"]
        lines = [f"{medals[i] if i < 3 else '▫️'} **{b.get('name') or b['playerId']}** — {b['pieces']} pièces"
                 for i, b in enumerate(builds)]
        await inter.response.send_message(embed=embed("🏗️ Bâtisseurs", chunks(lines)))


async def is_admin(bot: RatatoskrBot, user: discord.abc.User) -> bool:
    if not bot.admin_ids:
        info = await bot.application_info()
        bot.admin_ids = {m.id for m in info.team.members} if info.team else {info.owner.id}
    return user.id in bot.admin_ids


def register_admin(bot: RatatoskrBot) -> None:
    admin = app_commands.Group(name="admin", description="Commandes d'administration du serveur Valheim",
                               default_permissions=discord.Permissions(manage_guild=True), guild_only=True)

    async def guard(inter: discord.Interaction) -> bool:
        if await is_admin(bot, inter.user):
            return True
        await inter.response.send_message("⛔ Réservé à l'admin du serveur Valheim.", ephemeral=True)
        return False

    async def run(inter: discord.Interaction, line: str, done: str) -> None:
        try:
            reply = await bot.vh.rcon.command(line)
        except RconError as e:
            return await inter.followup.send(f"⚠️ `{e}`", ephemeral=True)
        log.info("admin %s (%s): %s -> %s", inter.user, inter.user.id, line, reply[:200])
        await inter.followup.send(f"{done}\n> {reply[:1800]}", ephemeral=True)

    async def online_player(inter: discord.Interaction, name: str) -> dict | None:
        players = await bot.vh.players()
        for p in players:
            if p["name"].lower() == name.lower():
                return p
        await inter.followup.send(f"❓ **{name}** n'est pas connecté.", ephemeral=True)
        return None

    async def player_autocomplete(inter: discord.Interaction, current: str):
        try:
            players = await bot.vh.players()
        except RconError:
            return []
        return [app_commands.Choice(name=p["name"], value=p["name"])
                for p in players if current.lower() in p["name"].lower()][:25]

    async def target_autocomplete(inter: discord.Interaction, current: str):
        choices = [app_commands.Choice(name="🪦 sa tombe la plus récente", value="tombe")]
        return choices + await player_autocomplete(inter, current)

    async def item_autocomplete(inter: discord.Interaction, current: str):
        try:
            items = await bot.stock_items()
        except RconError:
            return []
        cur = current.lower()
        return [app_commands.Choice(name=f"{i['name']} ({i['item']})", value=i["item"])
                for i in items if cur in i["name"].lower() or cur in i["item"].lower()][:25]

    @admin.command(name="save", description="Sauvegarde le monde maintenant")
    async def save(inter: discord.Interaction):
        if not await guard(inter):
            return
        await inter.response.defer(ephemeral=True)
        await run(inter, "save", "💾 Sauvegarde lancée.")

    @admin.command(name="annonce", description="Affiche un message au centre de l'écran de tous les joueurs")
    @app_commands.describe(texte="Le message")
    async def annonce(inter: discord.Interaction, texte: str):
        if not await guard(inter):
            return
        await inter.response.defer(ephemeral=True)
        await run(inter, f"showMessage {texte.replace(chr(34), '')}", "📣 Annonce envoyée.")

    @admin.command(name="kick", description="Expulse un joueur connecté")
    @app_commands.autocomplete(joueur=player_autocomplete)
    async def kick(inter: discord.Interaction, joueur: str):
        if not await guard(inter):
            return
        await inter.response.defer(ephemeral=True)
        p = await online_player(inter, joueur)
        if p:
            await run(inter, f"kick {p['host']}", f"👢 **{p['name']}** expulsé.")

    @admin.command(name="tp", description="Téléporte un joueur vers un autre joueur ou vers sa tombe")
    @app_commands.describe(joueur="Qui téléporter", vers="Un joueur connecté, ou « tombe »")
    @app_commands.autocomplete(joueur=player_autocomplete, vers=target_autocomplete)
    async def tp(inter: discord.Interaction, joueur: str, vers: str):
        if not await guard(inter):
            return
        await inter.response.defer(ephemeral=True)
        p = await online_player(inter, joueur)
        if not p:
            return
        if vers == "tombe":
            tombs = await bot.vh.tombs(p["name"])
            tombs = [t for t in tombs if t["owner"].lower() == p["name"].lower()]
            if not tombs:
                return await inter.followup.send(f"🪦 **{p['name']}** n'a pas de tombe.", ephemeral=True)
            x, y, z = tombs[0]["pos"]
            label = "sa tombe"
        else:
            target = await online_player(inter, vers)
            if not target:
                return
            x, y, z = target["pos"]
            label = f"**{target['name']}**"
        await run(inter, f"teleport {p['host']} {x:.1f} {y + 1:.1f} {z:.1f}",
                  f"✨ **{p['name']}** téléporté vers {label} ({where([x, y, z])}).")

    @admin.command(name="give", description="Donne un objet à un joueur connecté")
    @app_commands.describe(joueur="Destinataire", objet="Nom interne de l'objet (ex. Iron, Wood)", nombre="Quantité")
    @app_commands.autocomplete(joueur=player_autocomplete, objet=item_autocomplete)
    async def give(inter: discord.Interaction, joueur: str, objet: str,
                   nombre: app_commands.Range[int, 1, 999] = 1):
        if not await guard(inter):
            return
        await inter.response.defer(ephemeral=True)
        p = await online_player(inter, joueur)
        if p:
            item = objet.split()[0]
            await run(inter, f"give {p['host']} {item} -count {nombre}",
                      f"🎁 {item} ×{nombre} pour **{p['name']}**.")

    raid = app_commands.Group(name="raid", description="Raids en cours", parent=admin)

    @raid.command(name="stop", description="Arrête le raid en cours")
    async def raid_stop(inter: discord.Interaction):
        if not await guard(inter):
            return
        await inter.response.defer(ephemeral=True)
        await run(inter, "stopEvent", "🕊️ Raid arrêté.")

    bot.tree.add_command(admin)


def duration(seconds: int) -> str:
    h, m = divmod(int(seconds) // 60, 60)
    return f"{h} h {m:02d}" if h else f"{m} min"


class Watcher:
    def __init__(self, bot: RatatoskrBot):
        self.bot = bot
        self.boot: str | None = None
        self.seq = 0
        self.failures = 0
        self.down = False

    async def say(self, text: str) -> None:
        channel = self.bot.get_channel(self.bot.alert_channel_id)
        if channel is None:
            channel = await self.bot.fetch_channel(self.bot.alert_channel_id)
        await channel.send(text)

    async def run(self) -> None:
        await self.bot.wait_until_ready()
        while not self.bot.is_closed():
            try:
                await self.tick()
            except RconError as e:
                self.failures += 1
                log.warning("poll failed (%d): %s", self.failures, e)
                if self.failures == 3 and not self.down:
                    self.down = True
                    await self.say("🔴 Le serveur Valheim ne répond plus.")
                    await self.bot.change_presence(status=discord.Status.dnd,
                                                   activity=discord.Game("Valheim · hors ligne"))
            except Exception:
                log.exception("watcher tick crashed")
            await asyncio.sleep(self.bot.poll_seconds)

    async def tick(self) -> None:
        vh = self.bot.vh
        world = await vh.world()
        boot = world.get("boot")
        if self.boot is None:
            self.boot, self.seq = boot, world.get("seq", 0)
        elif boot != self.boot:
            # The server restarted and its sequence starts over.
            self.boot, self.seq = boot, 0
        events, _, _ = await vh.events(self.seq)
        self.failures = 0
        if self.down:
            self.down = False
            await self.say("🟢 Le serveur Valheim est de retour.")
        for ev in events:
            text = self.render(ev)
            if text:
                await self.say(text)
            self.seq = ev["seq"]

        players = world.get("players", 0)
        await self.bot.change_presence(
            status=discord.Status.online if players else discord.Status.idle,
            activity=discord.Game(f"Valheim · {players} en ligne · jour {world.get('day')}"))

    def render(self, ev: dict) -> str | None:
        kind, who = ev["type"], f"**{ev.get('player')}**"
        if kind == "join":
            return f"👋 Bienvenue {who} sur le serveur, pour la première fois !" if ev.get("first") \
                else f"➡️ {who} s'est connecté."
        if kind == "leave":
            return f"⬅️ {who} s'est déconnecté après {duration(ev.get('seconds', 0))}."
        if kind == "death":
            return f"💀 {who} est mort — {where(ev['pos'], ev.get('biome'))}."
        if kind == "biome":
            return f"🧭 {who} est le premier à poser le pied en **{biome(ev.get('biome'))}** !"
        if kind == "boss":
            return f"🏆 **{boss_name(ev['key'])}** a été vaincu !" + (f" Bravo {ev['online']} !" if ev.get("online") else "")
        if kind == "raid":
            near = f" près de **{ev['label']}**" if ev.get("label") else ""
            return f"⚔️ Raid{near} — {where(ev['pos'], ev.get('biome'))} : *{ev.get('message') or ev['raid']}*"
        if kind == "raidEnd":
            return "🕊️ Le raid est terminé."
        if kind == "chest" and self.bot.chest_alerts:
            moves = ", ".join(f"{'+' if c['delta'] > 0 else ''}{c['delta']} {c['name']}" for c in ev["changes"])
            return f"📦 {who} : {moves} ({ev.get('label') or ev['container'].replace('piece_', '')})"
        return None
