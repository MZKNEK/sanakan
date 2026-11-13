# Sanakan

[![tests](https://github.com/MZKNEK/sanakan/actions/workflows/tests.yml/badge.svg)](https://github.com/MZKNEK/sanakan/actions/workflows/tests.yml)
[![License: MPL 2.0](https://img.shields.io/badge/License-MPL_2.0-brightgreen.svg)](https://github.com/MZKNEK/sanakan/blob/master/LICENSE)

Sanakan is a Discord bot and web API built with [.NET 10](https://dotnet.microsoft.com/download/dotnet/10.0) and [Discord.NET](https://github.com/discord-net/Discord.Net).

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- MySQL-compatible database, or SQLite (no separate server needed)
- A Shinden API application token (`shk_…`) and, for operations not yet
  moved to the new API, the legacy Shinden API key
- A Discord bot token
- On Linux, systemd to run the bot as a service (`tools/install-service.sh`)

## Getting started

Clone the repository and change to the project directory:

```bash
git clone https://github.com/MZKNEK/sanakan.git
cd sanakan/src
```

### Configuration

Create `src/Config.json` from the following template. Replace the placeholder
values with your own values. The file must contain valid JSON, so do not add
comments to it.

```json
{
  "Prefix": ".",
  "BotToken": "your-discord-bot-token",
  "Supervision": true,
  "Demonization": false,
  "SafariEnabled": false,
  "AutoCleanCards": false,
  "DatabaseProvider": "MySql",
  "ConnectionString": "Server=localhost;Port=3306;Database=sanakan;User ID=sanakan;Password=your-password;",
  "CharPerPacket": 20000,
  "PacksPerDay": 0,
  "Shinden": {
    "ApiToken": "your-shinden-application-token",
    "Token": "your-shinden-legacy-api-key",
    "UserAgent": "your-user-agent"
  },
  "Exp": {
    "CharPerPoint": 60,
    "MinPerMessage": 0.00005,
    "MaxPerMessage": 5
  },
  "Dev": [
    123456789012345678
  ],
  "Jwt": {
    "Key": "your-jwt-signing-key",
    "Issuer": "your-issuer"
  },
  "GiveBanForUrlSpam": false,
  "UserKeyApps": [
    {
      "Key": "your-site-key",
      "Bearer": "your-site-name",
      "Permissions": "Site"
    },
    {
      "Key": "your-app-key",
      "Bearer": "your-app-name"
    }
  ],
  "AdminNotifyChannels": [
    {
      "GuildId": 123456789012345678,
      "ChannelId": 123456789012345678
    }
  ],
  "BlacklistedGuilds": [
    123456789012345678
  ]
}
```

To use SQLite instead of MySQL, set the provider and point the connection
string at a database file:

```json
  "DatabaseProvider": "Sqlite",
  "ConnectionString": "Data Source=sanakan.db",
```

A relative path is resolved against the working directory. The database
schema is created on the first start for both providers: on SQLite straight
from the model, on MySQL by the migrations (below). Data is not moved
between providers automatically. SQLite runs in WAL mode, so recent changes
may still be in the `-wal` file next to the database: back up all files
together, or only while the bot is stopped.

On SQLite the bot compares text like MySQL's default collation, ignoring
case and accents. External SQLite tools do not know these rules, so change
text columns only through the bot, or run `REINDEX NOCASE;` after editing
them elsewhere.

#### MySQL migrations

On MySQL the bot applies the pending migrations (`src/Database/Migrations`) when it starts and logs what it applied.
A database made before the migrations (an older bot, a dump of one) is recognised by its tables and taken as the
first migration, `Baseline`, without changing it. To apply them by hand instead, set `"MigrateOnStart": false`; the
bot then only logs which ones are missing.

The bot's MySQL account needs all rights on its own database, and the migrations create triggers. With binary
logging on (the MySQL 8 default) an account without `SUPER` may do that only with this in the server's `my.cnf`:

```ini
[mysqld]
log_bin_trust_function_creators = 1
```

A change to the model needs a migration, otherwise a test fails:

```bash
dotnet tool restore
dotnet ef migrations add <Name> --project src/Sanakan.csproj --output-dir Database/Migrations --namespace Sanakan.Database.Migrations
```

Keep migrations additive (new tables, columns, indexes) and drop things one version later: after a failed update
`Run.sh` starts the previous build, which then runs on the new schema.

A database from before the migrations differs from `Baseline` in a few details nothing depends on today. A migration
that changes one of them has to expect both forms:

- boolean columns are `bit(1)` there, `tinyint(1)` in the model;
- some columns have default values the model does not set (`Cards`/`Figures.CritChance`, `GameDecks` dates,
  `Guilds.AlwaysBanChannel`);
- the foreign keys of `ExpContainers`, `TagCardRelations` and `Tags` have MySQL's own names (`*_ibfk_*`);
- `Tags.Name` is `text NOT NULL`, `WishlistCountData.Id` has no `AUTO_INCREMENT`.

#### Game statistics

The bot keeps statistics of the game for charts (e.g. Grafana with a MySQL data source):

- `GameStats (Date, Metric, Label, Value)`, one value per day. Counted from what the saves really write, labelled
  with the command or API call (`api:Controller.Action`): `currency_in`/`currency_out` (`SC:drobne`),
  `card_in` (`A:Activity`, rarity and source), `card_out`, `card_upgrade` (`A>S`), `item_in`/`item_out`,
  `pack_in`/`pack_out`, `command`, `command_failed`, `command_error_reply`, `players_new`. Taken once a day for the
  day before: `players_active` (`30d` for the last 30 days), `retention` (`d1`, `d7`, `d30`, per mille of the new
  players of that day who came back), `newbies_d7`, `balance` (`TC:p50`… of the players of the last 30 days),
  `can_afford` (per mille of them who can pay for each shop item), `circulation`, `cards_total`, `items_total`,
  `packs_unopened`, `tc_transfer`.
- `PlayerDays (Date, UserId, Commands, IsNew)`: who used the bot on which day.
- `DuelResults`: every PvP duel with both decks' power, rank, MMR and rarities, the win chance the game gave the
  attacker and the outcome, without the players.

Counters are written every 10 minutes and when the bot stops; the daily values just after midnight, or a minute
after the start when the bot was down then. `PlayerDays`, `DuelResults` and the RAM measurements (`SystemData`) are
kept for a year.

#### Downloads from players' links (fetch proxy)

The bot downloads pictures from links players give (custom card images and borders, profile pictures). Such a
download shows the server's address to whoever runs the linked site. To hide it, send these downloads through
a fetch proxy:

```json
  "FetchProxy": {
    "Url": "http://<proxy host>/fetch",
    "Key": "<key from the proxy's owner>"
  },
```

Without `Url` everything is downloaded directly, as before. Hosts in `DirectHosts` are still downloaded directly,
together with their subdomains; without it they are `cdn.discordapp.com`, `media.discordapp.net` and
`cdn.shinden.eu` (card pictures, far too many for the proxy). `MaxBytes` is the largest file the proxy is asked
for (5 MB by default). `Mirrors` maps a host to an address its files are downloaded from instead, under the same
paths, e.g. `"Mirrors": { "cdn.shinden.eu": "http://<mirror host>/cdn/" }` when the server cannot reach that host
directly; it works without `Url` too, and the addresses kept in the database do not change. The bot sends at most
4 downloads to the proxy at once. When the proxy refuses the key or
cannot be reached, the log channel gets one error, and one line when it works again.

Keep `Config.json` out of source control. It contains bot credentials,
database credentials, and API keys. The repository's `.gitignore` should be
used to prevent accidental commits of local configuration.

### Build

From the `src` directory:

```bash
dotnet build -c Release
```

### First start on a server (Linux)

Log in as the user the bot will run as (not root), then:

1. Install the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) and git.
2. Get the code and enter it:
   ```bash
   git clone <repository address> sanakan
   cd sanakan
   ```
3. Put the configuration in `src/Config.json` (see [Configuration](#configuration)).
4. Check the server; it lists everything to fix (no `origin`, a branch `origin` does not have, no .NET SDK of the
   bot's framework, no config, a connection the bot needs that does not work, an old bot still running) and changes
   nothing. The connections are checked with `curl`, the addresses from the config (Shinden API, fetch proxy,
   heartbeat, ingest) also need `python3`:
   ```bash
   tools/check-server.sh
   ```
5. Register the bot as the `sanakan` service; this also gives your user the files and hides the config from others.
   It runs the same check first and stops when something is wrong:
   ```bash
   sudo tools/install-service.sh
   ```
6. Start it and watch the log:
   ```bash
   sudo systemctl start sanakan
   journalctl -u sanakan -f
   ```

The first start builds the bot, which takes a minute. From then on the service starts with the server.

Everyday commands:

| What | Command |
|---|---|
| status | `systemctl status sanakan` |
| restart | `sudo systemctl restart sanakan` |
| stop | `sudo systemctl stop sanakan` |
| log | `journalctl -u sanakan -f` |
| update | the bot's `update` command (or `POST api/debug/update`) |
| fix file owners after copying files by hand | `sudo tools/install-service.sh --permissions` |
| remove the service | `sudo tools/install-service.sh --remove` |

A Debug instance is the same with `debug`: `sudo tools/install-service.sh debug` registers `sanakan-debug`.

#### Generated pictures (GOut)

The bot keeps generated cards and the pictures players upload in `GOut` next to `src`. The database stores these
files as `../GOut/...`, so the folder has to stay reachable under that name. To keep it elsewhere, for example on
another disk, give the folder when installing; the script refuses a folder inside the repository:

```bash
sudo tools/install-service.sh --data /data/sanakan-GOut
```

It creates the folder and leaves a link in `GOut`'s place. An existing `GOut` is moved there when the folder is on
the same disk (stop the bot first); for another disk the script prints the commands to copy the pictures, which can
take long with millions of files. `GOut` lies outside `src`, so its files do not slow down building the bot, and git
ignores it.

#### Behind nginx

`tools/nginx-sanakan.conf` is a sample site for a server where the bot sits behind another nginx: it serves the
generated cards (`GOut/Cards`, never the players' pictures in `GOut/Saved`) for the CDN names in any letter case,
asks the bot to draw a card whose picture is missing (`api/waifu/card/{id}`), passes the API names to Kestrel on
`127.0.0.1:5000` with the client's address, and answers nothing for other names. The file says how to install it and
which values to fill in.

#### How updates and restarts work

The bot tells `src/Run.sh` what to do next with its exit code (`src/ExitCodes.cs`):

| Code | Meaning | What happens |
|---|---|---|
| 0 | stopped on purpose | stays down |
| 75 | something a new start can fix (lost Discord connection) | started again after 5 seconds |
| 78 | invalid configuration | stays down until fixed |
| 100 | update requested | new code is pulled, built and started |
| other | crash | started again after 5 seconds |

An update pulls the current branch from `origin` (fast-forward only) and builds it into a new folder under
`src/deploy/`. The bot switches to it only when the build succeeded; if the new build fails within a minute of
starting, the previous one is started again. The last three builds are kept. If the bot crashes 10 times within
10 minutes, systemd stops trying; `systemctl status sanakan` shows why.

#### Moving from the old Run.sh

Older versions ran `bin/Release/net6.0/Sanakan.dll` from a `Run.sh` loop that updated when it found the `updateNow`
file. That loop does not know the exit codes above: an update request (100) ends it and the bot stays down, so the
switch is done once by hand:

1. Install the .NET SDK of the bot's framework next to the old one; `dotnet --list-sdks` shows both.
2. Stop the old bot and the `Run.sh` loop that restarts it.
3. Check out the branch the server should follow (`git fetch origin && git checkout master`) and run
   `tools/check-server.sh`.
4. Go on with registering the service (step 5 of [First start on a server](#first-start-on-a-server-linux)).

To go back, stop the service (`sudo systemctl disable --now sanakan`), check out the old version and start the old
`Run.sh`; its build in `bin/Release/net6.0` is left as it was, the new builds go to `src/deploy/`.

### Run by hand

```bash
./Run.sh --loop          # restarts the bot like the service does
./Run.sh debug --loop    # Debug build
./Run.sh --update        # pull and build only, the next start uses the new build
```

Without a shell, run a build directly from `src` (the folder under `bin/Release` is named after `<TargetFramework>`
in `Sanakan.csproj`):

```bash
dotnet ./bin/Release/net10.0/Sanakan.dll
```

### Bot setup

Invite the bot to your Discord server and configure it with the `.mod`
commands. Run `.mod h` to display the available moderation commands.

## License

See [LICENSE](LICENSE) for the license terms. Fonts and libraries by others, with their licenses, are listed in
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
