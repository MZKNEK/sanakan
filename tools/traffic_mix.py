#!/usr/bin/env python3
"""Summarizes the bot's console log into a traffic mix: commands, executor tasks, Shinden calls and API traffic.

Usage: python3 traffic_mix.py LOG [LOG ...] [--json out.json] [--top N] [--year YYYY]
LOG can be a file, a .gz file or a directory (all files inside, non-recursive).
From systemd: journalctl -u <unit> --since "2026-11-02" > sanakan.log (any -o format works, -o cat loses old-format dates).
Both log formats are read: the current one ("[date] Cmd: u1 name (12ms)") and the older one
("Run cmd: u1 name" + "Executor: completed cmd-name in 12ms").
Only aggregates are printed; user ids never leave the script.
"""

import argparse
import gzip
import json
import os
import re
import sys
from collections import Counter, defaultdict
from datetime import datetime

MONTHS = {m: i + 1 for i, m in enumerate(["jan", "feb", "mar", "apr", "may", "jun", "jul", "aug", "sep", "oct", "nov", "dec"])}
MONTHS.update({m: i + 1 for i, m in enumerate(["sty", "lut", "mar", "kwi", "maj", "cze", "lip", "sie", "wrz", "paź", "lis", "gru"])})

# journalctl -o short ("Nov 02 08:00:19 host sanakan[123]: ") or short-iso ("2026-11-02T08:00:19+0100 host sanakan[123]: ")
JOURNAL = re.compile(r"^(?:(\S{3}) +(\d{1,2}) (\d{2}:\d{2}:\d{2})|(\d{4}-\d{2}-\d{2})T(\d{2}:\d{2}:\d{2})\S*) \S+ [^\s:\[]+(?:\[\d+\])?: ?(.*)$")
STAMP = re.compile(r"^\[(?:(\d{4}-\d{2}-\d{2}) )?(\d{2}:\d{2}:\d{2})\] (.*)$")
LABELS = [("📊 REPORT ", "report"), ("REPORT ", "report"), ("ERROR ", "error"), ("[REPORT] ", "report"), ("[ERROR] ", "error"), ("[ROUTINE] ", "routine")]

CMD = re.compile(r"^Cmd: u(\d+) (.+?)(?: \((\d+)ms\))?(?: - (.*))?$")
OLD_CMD = re.compile(r"^Run cmd: u(\d+) (.+?)\s*$")
EXEC = re.compile(r"^Exec: (.+?) \((\d+)ms\)(?: - (.*))?$")
OLD_EXEC = re.compile(r"^Executor: completed (.+?) in (\d+)ms")
SHINDEN = re.compile(r"^Shinden:? \[(\w+)\]: (\S+) \| (\d+|\w+) \| (\d+) ms")
API_WRITE = re.compile(r"^API: (?:u\d+ )?(\S+) ([A-Z]+) (\S+) -> (\d+) \((\d+)ms\)")
API_HEAD = re.compile(r"^API traffic \((\d+) min\): (\d+) requests")
API_ROW = re.compile(r"^\s*(\d+)\s+([A-Z]+)\s+(\S.*?)\s*$")
API_MORE = re.compile(r"^\(\+(\d+) more routes, (\d+) requests\)$")
NUMBER = re.compile(r"\d+")
NUMERIC_SEGMENT = re.compile(r"(?<=/)\d+(?=/|$)")
WEEKDAYS = ["pon", "wt", "śr", "czw", "pt", "sob", "niedz"]


def open_log(path):
    if path.endswith(".gz"):
        return gzip.open(path, "rt", encoding="utf-8", errors="replace")
    return open(path, "r", encoding="utf-8", errors="replace")


def expand(paths):
    for path in paths:
        if os.path.isdir(path):
            for name in sorted(os.listdir(path)):
                full = os.path.join(path, name)
                if os.path.isfile(full):
                    yield full
        else:
            yield path


def percentile(sorted_values, p):
    if not sorted_values:
        return None
    index = max(0, min(len(sorted_values) - 1, int(round(p / 100 * len(sorted_values) + 0.5)) - 1))
    return sorted_values[index]


def normalize_path(path):
    return NUMERIC_SEGMENT.sub("{n}", path.split("?")[0])


class Timing:
    def __init__(self):
        self.count = 0
        self.errors = 0
        self.ms = []
        self.users = set()
        self.statuses = Counter()

    def summary(self):
        ms = sorted(self.ms)
        result = {
            "count": self.count,
            "errors": self.errors,
            "users": len(self.users),
            "timed": len(ms),
            "avg_ms": round(sum(ms) / len(ms)) if ms else None,
            "p50_ms": percentile(ms, 50),
            "p95_ms": percentile(ms, 95),
            "max_ms": ms[-1] if ms else None,
        }
        if self.statuses:
            result["statuses"] = dict(self.statuses.most_common())
        return result


class Mix:
    def __init__(self, year):
        self.year = year
        self.commands = defaultdict(Timing)
        self.tasks = defaultdict(Timing)
        self.shinden = defaultdict(Timing)
        self.api_writes = defaultdict(Timing)
        self.per_user = Counter()
        self.per_hour = Counter()
        self.per_minute = Counter()
        self.formats = Counter()
        self.first = None
        self.last = None
        self.undated = 0
        self.api_routes = Counter()
        self.api_minutes = 0
        self.api_reports = 0
        self.api_other = 0
        self._api_open = False

    def journal_time(self, match):
        month, day, time, iso_date, iso_time = match.groups()[:5]
        try:
            if iso_date:
                return datetime.strptime(f"{iso_date} {iso_time}", "%Y-%m-%d %H:%M:%S")
            number = MONTHS.get(month.lower()[:3])
            if number is None:
                return None
            return datetime.strptime(f"{self.year}-{number:02d}-{int(day):02d} {time}", "%Y-%m-%d %H:%M:%S")
        except ValueError:
            return None

    def see(self, ts):
        if ts is None:
            return
        self.first = ts if self.first is None or ts < self.first else self.first
        self.last = ts if self.last is None or ts > self.last else self.last

    def feed(self, line):
        text = line.rstrip("\r\n")
        ts = None

        journal = JOURNAL.match(text)
        if journal:
            ts = self.journal_time(journal)
            text = journal.group(6)

        stamp = STAMP.match(text)
        if stamp:
            date, time, text = stamp.groups()
            if date:
                ts = datetime.strptime(f"{date} {time}", "%Y-%m-%d %H:%M:%S")

        if self._api_open and not stamp:
            if self.api_row(text):
                return
        self._api_open = False

        if not stamp and not journal:
            return  # continuation of a multi-line console entry (stack trace, report body)

        kind = "info"
        for prefix, name in LABELS:
            if text.startswith(prefix):
                kind, text = name, text[len(prefix):]
                break
        is_error = kind == "error"
        self.see(ts)

        cmd = CMD.match(text)
        if cmd:
            user, name, ms, _ = cmd.groups()
            self.formats["current"] += 1
            self.command(name, user, ts, is_error, int(ms) if ms is not None else None)
            return

        old = OLD_CMD.match(text)
        if old:
            self.formats["old"] += 1
            self.command(old.group(2), old.group(1), ts, is_error, None)
            return

        old_exec = OLD_EXEC.match(text)
        if old_exec:
            name, ms = old_exec.group(1), int(old_exec.group(2))
            if name.startswith("cmd-"):
                # the old format logs the command and its duration on separate lines; durations are matched by name
                self.commands[name[4:]].ms.append(ms)
            else:
                self.task(name, ms, is_error)
            return

        task = EXEC.match(text)
        if task:
            self.task(task.group(1), int(task.group(2)), is_error)
            return

        shinden = SHINDEN.match(text)
        if shinden:
            method, path, status, ms = shinden.groups()
            stats = self.shinden[f"{method} {normalize_path(path)}"]
            stats.count += 1
            stats.ms.append(int(ms))
            stats.statuses[status] += 1
            return

        write = API_WRITE.match(text)
        if write:
            who, method, path, status, ms = write.groups()
            stats = self.api_writes[f"{method} {normalize_path(path)}"]
            stats.count += 1
            stats.ms.append(int(ms))
            stats.statuses[status] += 1
            stats.users.add(who)
            return

        head = API_HEAD.match(text)
        if head and kind == "report":
            self.api_minutes += int(head.group(1))
            self.api_reports += 1
            self._api_open = True

    def command(self, name, user, ts, is_error, ms):
        stats = self.commands[name]
        stats.count += 1
        stats.users.add(user)
        stats.errors += 1 if is_error else 0
        if ms is not None:
            stats.ms.append(ms)
        self.per_user[user] += 1
        if ts is None:
            self.undated += 1
        else:
            self.per_hour[ts.replace(minute=0, second=0)] += 1
            self.per_minute[ts.replace(second=0)] += 1

    def task(self, name, ms, is_error):
        stats = self.tasks[NUMBER.sub("{n}", name)]
        stats.count += 1
        stats.errors += 1 if is_error else 0
        stats.ms.append(ms)

    def api_row(self, text):
        more = API_MORE.match(text.strip())
        if more:
            self.api_other += int(more.group(2))
            return True
        row = API_ROW.match(text)
        if row:
            self.api_routes[f"{row.group(2)} {row.group(3)}"] += int(row.group(1))
            return True
        return False

    def result(self):
        hours = (self.last - self.first).total_seconds() / 3600 if self.first and self.last else 0
        total = sum(x.count for x in self.commands.values())
        per_user = sorted(self.per_user.values(), reverse=True)
        days = max(1, len({x.date() for x in self.per_hour}))

        by_hour_of_day = [0] * 24
        by_weekday = [0] * 7
        for bucket, count in self.per_hour.items():
            by_hour_of_day[bucket.hour] += count
            by_weekday[bucket.weekday()] += count

        def share(top):
            return round(100 * sum(per_user[:top]) / total, 1) if total else 0

        def ordered(items):
            return {k: v.summary() for k, v in sorted(items.items(), key=lambda x: -x[1].count)}

        busiest_hour = max(self.per_hour.items(), key=lambda x: x[1]) if self.per_hour else None
        busiest_minute = max(self.per_minute.items(), key=lambda x: x[1]) if self.per_minute else None
        api_requests = sum(self.api_routes.values()) + self.api_other
        return {
            "span": {
                "from": self.first.isoformat(sep=" ") if self.first else None,
                "to": self.last.isoformat(sep=" ") if self.last else None,
                "hours": round(hours, 1),
                "days_with_commands": len({x.date() for x in self.per_hour}),
                "command_lines": dict(self.formats),
            },
            "commands": {
                "total": total,
                "undated": self.undated,
                "per_hour_avg": round(total / hours, 1) if hours else None,
                "busiest_hour": [busiest_hour[0].isoformat(sep=" "), busiest_hour[1]] if busiest_hour else None,
                "busiest_minute": [busiest_minute[0].isoformat(sep=" "), busiest_minute[1]] if busiest_minute else None,
                "avg_per_hour_of_day": [round(x / days, 1) for x in by_hour_of_day],
                "per_weekday": dict(zip(WEEKDAYS, by_weekday)),
                "by_name": ordered(self.commands),
            },
            "users": {
                "active": len(per_user),
                "commands_per_user_p50": percentile(sorted(per_user), 50),
                "commands_per_user_p90": percentile(sorted(per_user), 90),
                "commands_per_user_max": per_user[0] if per_user else None,
                "top10_share_pct": share(10),
                "top50_share_pct": share(50),
            },
            "executor_tasks": ordered(self.tasks),
            "shinden": {
                "calls": sum(x.count for x in self.shinden.values()),
                "per_hour_avg": round(sum(x.count for x in self.shinden.values()) / hours, 1) if hours else None,
                "by_route": ordered(self.shinden),
            },
            "api": {
                "reports": self.api_reports,
                "minutes": self.api_minutes,
                "requests": api_requests,
                "unlisted_requests": self.api_other,
                "per_hour_avg": round(api_requests * 60 / self.api_minutes, 1) if self.api_minutes else None,
                "by_route": dict(self.api_routes.most_common()),
                "writes": ordered(self.api_writes),
            },
        }


def fmt(value):
    return "-" if value is None else str(value)


def table(rows, right):
    widths = [max(len(r[i]) for r in rows) for i in range(len(rows[0]))]
    return "\n".join("  ".join(c.rjust(widths[i]) if right[i] else c.ljust(widths[i]) for i, c in enumerate(r)).rstrip() for r in rows)


def timing_table(items, top, users=False, statuses=False):
    total = sum(x["count"] for x in items.values())
    header = ["name", "count", "%"] + (["users"] if users else []) + ["err", "timed", "avg", "p50", "p95", "max"] + (["status"] if statuses else [])
    rows = [header]
    for name, s in list(items.items())[:top]:
        pct = f"{100 * s['count'] / total:.1f}" if total else "-"
        status = ",".join(f"{k}:{v}" for k, v in list(s.get("statuses", {}).items())[:3])
        rows.append([name, str(s["count"]), pct] + ([str(s["users"])] if users else [])
                    + [str(s["errors"]), str(s["timed"]), fmt(s["avg_ms"]), fmt(s["p50_ms"]), fmt(s["p95_ms"]), fmt(s["max_ms"])]
                    + ([status] if statuses else []))
    return table(rows, [False] + [True] * (len(header) - 1 - (1 if statuses else 0)) + ([False] if statuses else []))


def report(r, top):
    span, cmd, users, api, shinden = r["span"], r["commands"], r["users"], r["api"], r["shinden"]
    out = [
        f"Span: {fmt(span['from'])} -> {fmt(span['to'])} ({span['hours']} h, {span['days_with_commands']} days with commands), "
        + "command lines: " + (", ".join(f"{k} {v}" for k, v in span["command_lines"].items()) or "none"),
        "",
        f"Commands: {cmd['total']} total, {fmt(cmd['per_hour_avg'])}/h avg"
        + (f", {cmd['undated']} without a date" if cmd["undated"] else ""),
        f"Busiest hour: {fmt(cmd['busiest_hour'])}, busiest minute: {fmt(cmd['busiest_minute'])}",
        "Avg per hour of day: " + " ".join(f"{h:02d}:{v:g}" for h, v in enumerate(cmd["avg_per_hour_of_day"])),
        "Per weekday: " + ", ".join(f"{k} {v}" for k, v in cmd["per_weekday"].items()),
        "",
        timing_table(cmd["by_name"], top, users=True) if cmd["by_name"] else "(no commands)",
        "",
        f"Users: {users['active']} active, commands per user p50 {fmt(users['commands_per_user_p50'])}, "
        f"p90 {fmt(users['commands_per_user_p90'])}, max {fmt(users['commands_per_user_max'])}; "
        f"top 10 users {users['top10_share_pct']}% of commands, top 50 {users['top50_share_pct']}%",
    ]

    if r["executor_tasks"]:
        out += ["", "Executor tasks (without commands):", timing_table(r["executor_tasks"], top)]

    if shinden["by_route"]:
        out += ["", f"Shinden: {shinden['calls']} calls ({fmt(shinden['per_hour_avg'])}/h avg)", timing_table(shinden["by_route"], top, statuses=True)]

    out += ["", f"API: {api['requests']} requests in {api['reports']} hourly reports ({fmt(api['per_hour_avg'])}/h avg)"]
    if api["by_route"]:
        rows = [["requests", "/h", "route"]]
        for route, count in list(api["by_route"].items())[:top]:
            rows.append([str(count), f"{count * 60 / api['minutes']:.1f}" if api["minutes"] else "-", route])
        out.append(table(rows, [True, True, False]))
        if api["unlisted_requests"]:
            out.append(f"(+{api['unlisted_requests']} requests on routes outside the hourly top lists)")
    if api["writes"]:
        out += ["", "API writes (logged one by one):", timing_table(api["writes"], top, users=True, statuses=True)]
    return "\n".join(out)


def main():
    parser = argparse.ArgumentParser(description="Traffic mix from the bot's console log.")
    parser.add_argument("logs", nargs="+")
    parser.add_argument("--json", dest="json_out")
    parser.add_argument("--top", type=int, default=40)
    parser.add_argument("--year", type=int, default=datetime.now().year, help="year for journal dates without one")
    args = parser.parse_args()

    mix = Mix(args.year)
    for path in expand(args.logs):
        with open_log(path) as log:
            for line in log:
                mix.feed(line)

    result = mix.result()
    print(report(result, args.top))
    if args.json_out:
        with open(args.json_out, "w", encoding="utf-8") as f:
            json.dump(result, f, ensure_ascii=False, indent=2)
    return 0


if __name__ == "__main__":
    sys.exit(main())
