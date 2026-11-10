# Sanakan simulation

Runs the real bot against a fake Discord (gateway + REST) and a fake Shinden API on `127.0.0.1`,
so load and behaviour can be measured locally and compared between versions (.NET, database provider).
The bot itself is unchanged; it only gets `DiscordGatewayUrl` / `DiscordApiUrl` in its config.

Outgoing traffic of the bot is contained: `HTTPS_PROXY` points to a closed local port, `HTTP_PROXY` to the
simulator, which serves Shinden pictures (`cdn.shinden.eu` is plain http) and answers 404 to anything else.

## Modes

```
dotnet build src/Sanakan.csproj -c Release
dotnet run --project Simulation -- smoke                  # fresh SQLite database, a few commands
dotnet run --project Simulation -- init                   # fresh database, the bot creates the schema and stops
dotnet run --project Simulation -- load --world W --mix M # replays the production traffic mix
```

`load` options: `--minutes 10` (real duration), `--speed 1` (virtual hours per real hour, drives the hour-of-day
profile), `--scale 1` (traffic multiplier), `--chat 2300` (chat messages per hour at the average hour),
`--start-hour 20`.

## Production data (SQLite)

1. Export the production database (phpMyAdmin: custom export, SQL, no `CREATE DATABASE / USE`).
2. `dotnet run --project Simulation -- init`
3. `python tools/mysql_dump_to_sqlite.py dump.sql Simulation/run/bot/sim.db --world Simulation/run/world.json`
   (empties the SQLite tables first, checks row counts; run it again to reset the copy before each comparison)
4. `python tools/traffic_mix.py <bot console log> --json Simulation/run/mix.json`
5. `dotnet run --project Simulation -- load --world Simulation/run/world.json --mix Simulation/run/mix.json`

Results go to `Simulation/run/results/load-*.json`: commands sent and answered, reply times (only replies that
mention the author are timed), bot memory and CPU, requests the bot made, grouped bot errors.

`Simulation/run/` holds real player ids and the database copy; it is ignored by git and must stay local.

## Limits

- Discord CDN (avatars) is https and stays blocked, image generation falls back like on a CDN outage.
- Command arguments come from a sample of each player's cards and items; destructive commands change the copy.
- Replies without a mention are paired with commands by order in the channel, their times are not reported.
- SQLite shows behaviour, not MySQL performance; compare versions on the same database engine.
