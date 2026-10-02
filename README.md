# Sanakan

[![Build status](https://img.shields.io/appveyor/build/MrZnake/sanakan)](https://ci.appveyor.com/project/mrznake/sanakan/branch/master)
[![CodeFactor](https://www.codefactor.io/repository/github/mzknek/sanakan/badge)](https://www.codefactor.io/repository/github/mzknek/sanakan)
[![License](https://img.shields.io/github/license/MZKNEK/sanakan)](https://github.com/MZKNEK/sanakan/blob/master/LICENSE)

Sanakan is a Discord bot and web API built with [.NET 6](https://dotnet.microsoft.com/download/dotnet/6.0) and [Discord.NET](https://github.com/discord-net/Discord.Net).

## Requirements

- [.NET 6 SDK](https://dotnet.microsoft.com/download/dotnet/6.0)
- MySQL-compatible database
- A Shinden API key
- A Discord bot token
- `make` for the Makefile commands

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
  "ConnectionString": "Server=localhost;Port=3306;Database=sanakan;User ID=sanakan;Password=your-password;",
  "CharPerPacket": 20000,
  "PacksPerDay": 0,
  "Shinden": {
    "Token": "your-shinden-api-key",
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
  "ApiKeys": [
    {
      "Key": "your-api-key",
      "Bearer": "your-bearer-name"
    }
  ],
  "RMConfig": [
    {
      "RoleId": 123456789012345678,
      "GuildId": 123456789012345678,
      "ChannelId": 123456789012345678,
      "Type": "News"
    }
  ],
  "BlacklistedGuilds": [
    123456789012345678
  ]
}
```

Keep `Config.json` out of source control. It contains bot credentials,
database credentials, and API keys. The repository's `.gitignore` should be
used to prevent accidental commits of local configuration.

### Build

From the `src` directory, restore dependencies and build the Release version:

```bash
make full-build
```

The equivalent .NET commands are:

```bash
dotnet restore
dotnet build -c Release
```

To build the Debug version:

```bash
make restore
make build-debug
```

### Run

From the `src` directory, run the Release build with:

```bash
./Run.sh
```

For a Debug build, use:

```bash
./RunDebug.sh
```

The scripts restart the application after an update when the `updateNow`
marker file is present. On systems where shell scripts are not available, run
the compiled application directly:

```bash
dotnet ./bin/Release/net6.0/Sanakan.dll
```

### Bot setup

Invite the bot to your Discord server and configure it with the `.mod`
commands. Run `.mod h` to display the available moderation commands.

## License

See [LICENSE](LICENSE) for the license terms.
