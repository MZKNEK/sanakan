# Sanakan

[![tests](https://github.com/MZKNEK/sanakan/actions/workflows/tests.yml/badge.svg)](https://github.com/MZKNEK/sanakan/actions/workflows/tests.yml)
[![CodeFactor](https://www.codefactor.io/repository/github/mzknek/sanakan/badge)](https://www.codefactor.io/repository/github/mzknek/sanakan)
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
schema is created on the first start for both providers. Data is not moved
between providers automatically. SQLite runs in WAL mode, so recent changes
may still be in the `-wal` file next to the database: back up all files
together, or only while the bot is stopped.

On SQLite the bot compares text like MySQL's default collation, ignoring
case and accents. External SQLite tools do not know these rules, so change
text columns only through the bot, or run `REINDEX NOCASE;` after editing
them elsewhere.

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
4. Register the bot as the `sanakan` service; this also gives your user the files and hides the config from others:
   ```bash
   sudo tools/install-service.sh
   ```
5. Start it and watch the log:
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
