#!/usr/bin/env python3
"""Loads a phpMyAdmin/mysqldump SQL dump of the bot's MySQL database into a SQLite database
whose schema the bot already created (EnsureCreated), and writes the ids the simulator needs.

Usage: python3 mysql_dump_to_sqlite.py DUMP.sql[.gz] TARGET.db [--world world.json] [--skip SystemData,...]

Every table of TARGET.db is emptied first. Only columns present in both the dump and the SQLite schema are copied;
the differences are reported. world.json holds real guild, channel, role and user ids - keep it out of the repository.
"""

import argparse
import gzip
import json
import random
import re
import sqlite3
import sys
import time
from collections import defaultdict

INSERT = re.compile(r"^INSERT INTO `([^`]+)` \(([^)]*)\) VALUES\s*(.*)$")
TOKEN = re.compile(r"NULL|'(?:[^'\\]|\\.|'')*'|b'[01]'|0x[0-9A-Fa-f]*|-?\d+(?:\.\d+)?(?:[eE][-+]?\d+)?")
ESCAPE = re.compile(r"\\(.)|''", re.S)
ESCAPES = {"0": "\0", "b": "\b", "n": "\n", "r": "\r", "t": "\t", "Z": "\x1a"}
ULONG_LIMIT = 2 ** 63
BATCH = 5000


def open_dump(path):
    if path.endswith(".gz"):
        return gzip.open(path, "rt", encoding="utf-8", errors="strict")
    return open(path, "r", encoding="utf-8", errors="strict")


def unescape(match):
    char = match.group(1)
    if char is None:
        return "'"
    return ESCAPES.get(char, char)


def parse_value(token):
    if token == "NULL":
        return None
    if token[0] == "'":
        return ESCAPE.sub(unescape, token[1:-1])
    if token.startswith("b'"):
        return int(token[2])
    if token.startswith("0x"):
        return bytes.fromhex(token[2:])
    if "." in token or "e" in token or "E" in token:
        return float(token)
    value = int(token)
    # EF Core stores ulong in SQLite as the same 64 bits read as a signed long
    return value - 2 ** 64 if value >= ULONG_LIMIT else value


def parse_row(text):
    start = text.index("(") + 1
    end = text.rindex(")")
    return [parse_value(m.group(0)) for m in TOKEN.finditer(text, start, end)]


class World:
    """Collects ids from configuration tables and a sample of each player's state while the dump streams by."""

    CARDS_PER_DECK = 40
    PLAYERS_WITH_STATE = 500

    def __init__(self):
        self.guilds = {}
        self.waifus = {}
        self.channel_rows = defaultdict(list)
        self.role_rows = defaultdict(list)
        self.users = []
        self.deck_owner = {}
        self.deck_cards = defaultdict(list)
        self.deck_card_count = defaultdict(int)
        self.deck_expeditions = defaultdict(list)
        self.deck_items = defaultdict(int)
        self.random = random.Random(13)

    def see_card(self, card_id, deck, expedition):
        count = self.deck_card_count[deck] = self.deck_card_count[deck] + 1
        sample = self.deck_cards[deck]
        if len(sample) < self.CARDS_PER_DECK:
            sample.append(card_id)
        else:
            slot = self.random.randrange(count)
            if slot < self.CARDS_PER_DECK:
                sample[slot] = card_id
        if expedition and len(self.deck_expeditions[deck]) < 10:
            self.deck_expeditions[deck].append(card_id)

    def see(self, table, columns, row):
        data = dict(zip(columns, row))
        if table == "GameDecks":
            self.deck_owner[data["Id"]] = data["UserId"]
        elif table == "Items":
            self.deck_items[data["GameDeckId"]] += 1
        elif table == "Guilds":
            self.guilds[data["Id"]] = data
        elif table == "Waifus":
            self.waifus[data["Id"]] = data
        elif table == "Users":
            self.users.append({"id": data["Id"], "commands": data.get("CommandsCnt") or 0, "messages": data.get("MessagesCnt") or 0})
        if "Channel" in data and table not in ("Guilds", "Waifus"):
            self.channel_rows[table].append(data)
        if "Role" in data and table != "Guilds":
            self.role_rows[table].append(data)

    def build(self):
        guilds = []
        for gid, guild in self.guilds.items():
            waifu = next((w for w in self.waifus.values() if w.get("GuildOptionsId") == gid), {})
            waifu_ids = {w["Id"] for w in self.waifus.values() if w.get("GuildOptionsId") == gid}
            channels = {k: v for k, v in guild.items() if k.endswith("Channel") and v}
            channels.update({f"Waifu{k}": v for k, v in waifu.items() if k.endswith("Channel") and v})
            lists = {}
            for table, rows in self.channel_rows.items():
                ids = [r["Channel"] for r in rows if r.get("GuildOptionsId") == gid or r.get("WaifuId") in waifu_ids]
                if ids:
                    lists[table] = ids
            roles = {k: v for k, v in guild.items() if k.endswith("Role") and v}
            role_lists = {t: [r["Role"] for r in rows if r.get("GuildOptionsId") == gid] for t, rows in self.role_rows.items()}
            guilds.append({"id": gid, "prefix": guild.get("Prefix"), "channels": channels, "channel_lists": lists,
                           "roles": roles, "role_lists": {k: v for k, v in role_lists.items() if v}})
        guilds.sort(key=lambda g: -len(g["channel_lists"].get("WaifuCommandChannels", [])))
        users = sorted(self.users, key=lambda u: -u["commands"])
        decks = {owner: deck for deck, owner in self.deck_owner.items()}
        for user in users[:self.PLAYERS_WITH_STATE]:
            deck = decks.get(user["id"])
            user["cards"] = self.deck_cards.get(deck, [])
            user["card_count"] = self.deck_card_count.get(deck, 0)
            user["expedition_cards"] = self.deck_expeditions.get(deck, [])
            user["items"] = self.deck_items.get(deck, 0)
        return {"guilds": guilds, "users": users}


def main():
    parser = argparse.ArgumentParser(description="MySQL dump -> SQLite database created by the bot.")
    parser.add_argument("dump")
    parser.add_argument("target")
    parser.add_argument("--world")
    parser.add_argument("--skip", default="SystemData", help="comma separated tables not to copy")
    args = parser.parse_args()
    skip = {x for x in args.skip.split(",") if x}

    db = sqlite3.connect(args.target)
    db.execute("PRAGMA foreign_keys = OFF")
    db.execute("PRAGMA synchronous = OFF")
    tables = [r[0] for r in db.execute("SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' AND name <> '__EFMigrationsHistory'")]
    if not tables:
        print(f"{args.target} has no tables, start the bot once on it so it creates the schema", file=sys.stderr)
        return 1

    schema = {}
    for table in tables:
        info = db.execute(f'PRAGMA table_info("{table}")').fetchall()
        schema[table] = {r[1]: {"type": (r[2] or "").upper(), "notnull": bool(r[3]), "default": r[4], "pk": bool(r[5])} for r in info}
        db.execute(f'DELETE FROM "{table}"')
    db.commit()

    world = World()
    copied = defaultdict(int)
    bad_rows = defaultdict(int)
    skipped_tables = defaultdict(int)
    dropped_columns = {}
    filled_columns = {}
    started = time.time()

    table = None
    columns = None
    plan = None
    for_world = False
    card_columns = None
    batch = []

    dumped = defaultdict(int)

    def flush():
        if batch and plan:
            db.executemany(plan["sql"], batch)
            copied[plan["table"]] += len(batch)
            batch.clear()

    with open_dump(args.dump) as dump:
        for line in dump:
            if line.startswith("INSERT INTO"):
                flush()
                match = INSERT.match(line.rstrip("\n"))
                table = match.group(1)
                columns = [c.strip().strip("`") for c in match.group(2).split(",")]
                for_world = table in ("Guilds", "Waifus", "Users", "GameDecks", "Items") or "Channel" in columns or "Role" in columns
                card_columns = [columns.index(c) for c in ("Id", "GameDeckId", "Expedition")] if table == "Cards" else None
                plan = None
                if table not in skip and table in schema:
                    target = schema[table]
                    keep = [i for i, c in enumerate(columns) if c in target]
                    dropped_columns[table] = [c for c in columns if c not in target]
                    missing = [c for c, d in target.items() if c not in columns and d["notnull"] and d["default"] is None and not d["pk"]]
                    filled_columns[table] = missing
                    fill = [0.0 if "REAL" in target[c]["type"] else 0 if "INT" in target[c]["type"] else "" for c in missing]
                    names = [columns[i] for i in keep] + missing
                    plan = {
                        "table": table,
                        "keep": keep,
                        "fill": fill,
                        "sql": f'INSERT INTO "{table}" ({", ".join(f"[{n}]" for n in names)}) VALUES ({", ".join("?" * len(names))})',
                    }
                rest = match.group(3)
                if not rest.startswith("("):
                    continue
                line = rest

            if table is None or not line.startswith("("):
                if line.startswith("CREATE TABLE") or line.startswith("--"):
                    table = None
                continue

            row = parse_row(line)
            dumped[table] += 1
            if len(row) != len(columns):
                bad_rows[table] += 1
                continue

            if for_world:
                world.see(table, columns, row)
            elif card_columns:
                world.see_card(row[card_columns[0]], row[card_columns[1]], row[card_columns[2]])
            if plan is None:
                skipped_tables[table] += 1
                continue

            batch.append([row[i] for i in plan["keep"]] + plan["fill"])
            if len(batch) >= BATCH:
                flush()
                db.commit()
        flush()
    db.commit()

    print(f"Imported in {time.time() - started:.0f} s into {args.target}")
    mismatched = []
    for name in sorted(copied, key=lambda x: -copied[x]):
        stored = db.execute(f'SELECT COUNT(*) FROM "{name}"').fetchone()[0]
        if stored != dumped[name]:
            mismatched.append(f"{name} (dump {dumped[name]}, sqlite {stored})")
        notes = []
        if dropped_columns.get(name):
            notes.append("not in the model: " + ", ".join(dropped_columns[name]))
        if filled_columns.get(name):
            notes.append("filled with defaults: " + ", ".join(filled_columns[name]))
        print(f"  {copied[name]:>9}  {name}" + (f"  ({'; '.join(notes)})" if notes else ""))
    for name, count in sorted(skipped_tables.items()):
        print(f"  skipped {count:>9}  {name}" + ("" if name in skip else "  (no such table in the model)"))
    for name, count in sorted(bad_rows.items()):
        print(f"  BAD ROWS {count:>8}  {name}  (value count did not match the columns)")
    empty = [t for t in tables if copied.get(t, 0) == 0]
    if empty:
        print("  empty in SQLite: " + ", ".join(sorted(empty)))
    print("  row counts: " + ("ALL MATCH the dump" if not mismatched else "DIFFER for " + ", ".join(mismatched)))

    if args.world:
        data = world.build()
        with open(args.world, "w", encoding="utf-8") as f:
            json.dump(data, f, indent=1)
        main_guild = data["guilds"][0] if data["guilds"] else None
        print(f"World: {len(data['guilds'])} guild configs, {len(data['users'])} users"
              + (f"; main guild has {len(main_guild['channel_lists'].get('WaifuCommandChannels', []))} waifu command channels" if main_guild else ""))
    return 1 if bad_rows else 0


if __name__ == "__main__":
    sys.exit(main())
