#!/usr/bin/env python3
"""Keeps the MySQL databases of the simulation: a pristine copy of the production dump and the copy the bot changes.

Usage:
  python3 mysql_sim_db.py load-base DUMP.sql   # imports the dump into the base database (once, slow)
  python3 mysql_sim_db.py reset                # recreates the simulation database from the base one (before each run)
  python3 mysql_sim_db.py counts               # row counts of both databases

The connection comes from SANAKAN_SIM_MYSQL (Server=...;Port=...;Database=<simulation db>;User=...;Password=...),
read from the process environment or, on Windows, from the user environment. The base database is <simulation db>_base.
The password is passed to the mysql client in a temporary option file, never on the command line.
"""

import glob
import os
import re
import shutil
import subprocess
import sys
import tempfile
import time

VARIABLE = "SANAKAN_SIM_MYSQL"


def connection():
    value = os.environ.get(VARIABLE)
    if not value and sys.platform == "win32":
        import winreg
        try:
            with winreg.OpenKey(winreg.HKEY_CURRENT_USER, "Environment") as key:
                value = winreg.QueryValueEx(key, VARIABLE)[0]
        except OSError:
            value = None
    if not value:
        sys.exit(f"{VARIABLE} is not set")
    parts = {}
    for item in value.split(";"):
        if "=" in item:
            k, v = item.split("=", 1)
            parts[k.strip().lower()] = v.strip()
    return parts


class Client:
    def __init__(self):
        conn = connection()
        self.database = conn.get("database", "sanakan_sim")
        self.base = f"{self.database}_base"
        handle, self.options = tempfile.mkstemp(suffix=".cnf")
        with os.fdopen(handle, "w", encoding="utf-8") as f:
            f.write("[client]\n")
            f.write(f"host={conn.get('server', '127.0.0.1')}\nport={conn.get('port', '3306')}\n")
            f.write(f"user={conn.get('user', conn.get('uid', 'root'))}\npassword={conn.get('password', conn.get('pwd', ''))}\n")
            f.write("default-character-set=utf8mb4\n")

    def close(self):
        os.remove(self.options)

    def run(self, sql, database=None, stdin=None):
        # batch mode escapes newlines, tabs and backslashes in values, rows() undoes it
        args = [mysql_client(), f"--defaults-extra-file={self.options}", "--batch", "--skip-column-names"]
        if database:
            args.append(database)
        if sql is not None:
            args += ["-e", sql]
        result = subprocess.run(args, stdin=stdin, capture_output=True, text=True, encoding="utf-8")
        if result.returncode != 0:
            sys.exit(f"mysql failed: {result.stderr.strip()}")
        return result.stdout

    def rows(self, sql, database=None):
        return [[unescape(v) for v in line.split("\t")] for line in self.run(sql, database).splitlines() if line]


def mysql_client():
    # a console opened before MySQL was added to PATH does not see it, the default install folder is checked too
    found = shutil.which("mysql")
    if found:
        return found
    installed = sorted(glob.glob(r"C:\Program Files\MySQL\MySQL Server *\bin\mysql.exe"))
    if installed:
        return installed[-1]
    sys.exit("mysql client not found, add its bin folder to PATH")


def unescape(value):
    return re.sub(r"\\(.)", lambda m: {"n": "\n", "t": "\t", "0": "\0"}.get(m.group(1), m.group(1)), value)


def load_base(client, dump):
    started = time.time()
    client.run(f"DROP DATABASE IF EXISTS `{client.base}`; CREATE DATABASE `{client.base}`")
    with open(dump, "rb") as f:
        client.run(None, client.base, stdin=f)
    print(f"{dump} -> {client.base} in {time.time() - started:.0f} s")


def reset(client):
    started = time.time()
    tables = [r[0] for r in client.rows(f"SELECT table_name FROM information_schema.tables WHERE table_schema = '{client.base}' AND table_type = 'BASE TABLE'")]
    if not tables:
        sys.exit(f"{client.base} is empty, run load-base first")

    statements = [f"DROP DATABASE IF EXISTS `{client.database}`", f"CREATE DATABASE `{client.database}`", f"USE `{client.database}`",
                  "SET FOREIGN_KEY_CHECKS = 0", "SET UNIQUE_CHECKS = 0"]
    for table in tables:
        create = client.rows(f"SHOW CREATE TABLE `{client.base}`.`{table}`")[0][1]
        statements.append(create)
        statements.append(f"INSERT INTO `{table}` SELECT * FROM `{client.base}`.`{table}`")
    for name, in client.rows(f"SELECT trigger_name FROM information_schema.triggers WHERE trigger_schema = '{client.base}'"):
        # single-statement triggers only (the dump has one); a BEGIN ... END body would need a delimiter
        body = client.rows(f"SHOW CREATE TRIGGER `{client.base}`.`{name}`")[0][2]
        statements.append(body)
    statements += ["SET UNIQUE_CHECKS = 1", "SET FOREIGN_KEY_CHECKS = 1"]
    client.run(";\n".join(statements))
    print(f"{client.base} -> {client.database}: {len(tables)} tables in {time.time() - started:.0f} s")
    counts(client)


def counts(client):
    def tables(database):
        return {r[0] for r in client.rows(f"SELECT table_name FROM information_schema.tables WHERE table_schema = '{database}' AND table_type = 'BASE TABLE'")}

    base, sim = tables(client.base), tables(client.database)
    differ = []
    for table in sorted(base):
        a = int(client.rows(f"SELECT COUNT(*) FROM `{client.base}`.`{table}`")[0][0])
        b = int(client.rows(f"SELECT COUNT(*) FROM `{client.database}`.`{table}`")[0][0]) if table in sim else None
        if a != b:
            differ.append(f"{table} ({a} vs {b})")
    print("row counts: " + ("ALL MATCH" if not differ else "DIFFER for " + ", ".join(differ)))


def main():
    if len(sys.argv) < 2 or sys.argv[1] not in ("load-base", "reset", "counts") or (sys.argv[1] == "load-base" and len(sys.argv) < 3):
        print(__doc__)
        return 1
    client = Client()
    try:
        if sys.argv[1] == "load-base":
            load_base(client, sys.argv[2])
        elif sys.argv[1] == "reset":
            reset(client)
        else:
            counts(client)
    finally:
        client.close()
    return 0


if __name__ == "__main__":
    sys.exit(main())
