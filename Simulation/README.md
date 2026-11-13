# Sanakan simulation

Runs the real bot against a fake Discord (gateway + REST) and a fake Shinden API on `127.0.0.1`,
so load and behaviour can be measured locally and compared between versions (.NET, database provider).
The bot itself is unchanged; it only gets `DiscordGatewayUrl` / `DiscordApiUrl` in its config.

Outgoing traffic of the bot is contained: `HTTPS_PROXY` points to a closed local port, `HTTP_PROXY` to the
simulator, which serves Shinden pictures (`cdn.shinden.eu` is plain http) and answers 404 to anything else.
Like on the production server, the bot gets Shinden's pictures from a local mirror (`FetchProxy.Mirrors`), here the
simulator's `/cdn/`.
Downloads from players' links go through a fake fetch proxy (`/fetch`, `FetchProxy` in the bot's config) like on
the production server: it answers a picture for any address, 404 for addresses with `missing` in them, and `busy`
above 4 requests at once. `--extra "konfiguracja profilu"` sends profile backgrounds linked to `img.sim.test`
through it.

## Modes

```
dotnet build src/Sanakan.csproj -c Release
dotnet run --project Simulation -- smoke                  # fresh SQLite database, a few commands
dotnet run --project Simulation -- init                   # fresh database, the bot creates the schema and stops
dotnet run --project Simulation -- load --world W --mix M # replays the production traffic mix
```

`load` options: `--minutes 10` (real duration), `--speed 1` (virtual hours per real hour, drives the hour-of-day
profile), `--scale 1` (traffic multiplier), `--chat 2300` (chat messages per hour at the average hour),
`--start-hour 20`, `--extra "jakie,czego brak"` (commands sent on top of the mix, one every 10 s, so the rare ones
changed in a commit are exercised; each needs a template in `CommandCatalog.cs`).

Web API: `--api` adds the API traffic of the mix (hourly API reports and logged writes) for the routes with a template
in `ApiCatalog.cs`, at the same speed and scale as the commands. `--extra` also takes `api:` names (e.g.
`api:deck,api:pack-open`), one every 2 s. The bot listens on `http://localhost:5000`; the simulation calls it with an
app key (Site and Info) and with player tokens it signs itself, both written into the bot's config. The result has
status codes and times per route under `api`.

## Production data (SQLite)

1. Export the production database (phpMyAdmin: custom export, SQL, no `CREATE DATABASE / USE`).
2. `dotnet run --project Simulation -- init`
3. `python tools/mysql_dump_to_sqlite.py dump.sql Simulation/run/bot/sim.db --world Simulation/run/world.json`
   (empties the SQLite tables first, checks row counts; run it again to reset the copy before each comparison)
4. `python tools/traffic_mix.py <bot console log> --json Simulation/run/mix.json`
5. `dotnet run --project Simulation -- load --world Simulation/run/world.json --mix Simulation/run/mix.json`

Results go to `Simulation/run/results/load-*.json`: commands sent and answered, reply times (only replies that
mention the author are timed), bot memory and CPU in cores (with `--mysql` also the local
mysqld's and both together; 5 s samples), requests the bot made, grouped bot errors.

`Simulation/run/` holds real player ids and the database copy; it is ignored by git and must stay local.

## Limits

- Discord CDN (avatars) is https and stays blocked, image generation falls back like on a CDN outage.
- Command arguments come from a sample of each player's cards and items; destructive commands change the copy.
- Replies without a mention are paired with commands by order in the channel, their times are not reported.
- SQLite shows behaviour, not MySQL performance; compare versions on the same database engine.
