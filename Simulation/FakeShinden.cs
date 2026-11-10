using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;

namespace Sanakan.Simulation;

// Shinden API and image CDN with generated data: every character id exists, characters are spread over titles
// by id, and response times follow the medians seen in the production logs (2026-11-02..10).
public sealed class FakeShinden
{
    public const string Prefix = "/shinden";
    public const string ImageHost = "cdn.shinden.eu";

    private const ulong FirstTitle = 1000;

    private readonly SimLog _log;
    private readonly int _characters;
    private readonly int _titles;
    private readonly bool _delays;
    private readonly List<byte[]> _images;
    private readonly ConcurrentDictionary<string, byte[]> _flatLists = new();

    public FakeShinden(SimLog log, int characters = 20000, int titles = 500, bool delays = true)
    {
        _log = log;
        _characters = characters;
        _titles = titles;
        _delays = delays;
        _images = Enumerable.Range(0, 16).Select(CreateImage).ToList();
    }

    public async Task HandleAsync(HttpContext context)
    {
        var path = context.Request.Path.Value![Prefix.Length..].TrimEnd('/');
        _log.Route("shinden", context.Request.Method, path);
        var segments = path.Trim('/').Split('/');

        JsonNode body = segments switch
        {
            ["health"] => new JsonObject { ["status"] = "ok" },
            ["compat", "php-api", "character", "flat-list" or "flat-list-anime"] => null,
            ["compat", "php-api", "character", var id] when ulong.TryParse(id, out var characterId) => Character(characterId),
            ["compat", "php-api", "title", var id, "characters"] when ulong.TryParse(id, out var titleId) => TitleCharacters(titleId),
            ["compat", "php-api", "title", var id, "info"] when ulong.TryParse(id, out var titleId) => TitleInfo(titleId),
            ["compat", "php-api", "user", var id, "info"] when ulong.TryParse(id, out var userId) => User(userId),
            ["compat", "php-api", "user", _, "last_view" or "last_read"] => new JsonArray(),
            ["compat", "php-api", "anime", "current-season"] => Season(),
            ["v1", "search", ..] => new JsonArray(),
            _ => null,
        };

        await DelayAsync(segments);

        if (segments is ["compat", "php-api", "character", "flat-list" or "flat-list-anime"])
        {
            context.Response.ContentType = "application/json";
            await context.Response.Body.WriteAsync(_flatLists.GetOrAdd(segments[^1], _ => System.Text.Encoding.UTF8.GetBytes(FlatList().ToJsonString())));
            return;
        }

        if (body == null)
        {
            context.Response.StatusCode = 404;
            await context.Response.WriteAsJsonAsync(new { error = "not modelled by the simulator" });
            return;
        }

        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(body.ToJsonString());
    }

    // reached through the bot's HTTP proxy setting, Shinden serves pictures over plain http
    public async Task HandleImageAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "";
        _log.Route("shinden-cdn", context.Request.Method, path);
        var digits = new string(Path.GetFileNameWithoutExtension(path).TakeWhile(char.IsDigit).ToArray());
        var index = ulong.TryParse(digits, out var id) ? (int)(id % (ulong)_images.Count) : 0;

        if (_delays)
            await Task.Delay(Jitter(40));

        context.Response.ContentType = "image/jpeg";
        await context.Response.Body.WriteAsync(_images[index]);
    }

    private Task DelayAsync(string[] segments)
    {
        if (!_delays) return Task.CompletedTask;

        var median = segments switch
        {
            [.., "flat-list" or "flat-list-anime"] => 1600,
            [.., "characters"] => 81,
            [.., "last_view" or "last_read"] => 125,
            [.., "character", _] => 65,
            _ => 60,
        };
        return Task.Delay(Jitter(median));
    }

    private static int Jitter(int median) => (int)(median * (0.75 + Random.Shared.NextDouble() * 0.6));

    private ulong TitleOf(ulong character) => FirstTitle + character % (ulong)_titles;

    private static string TitleName(ulong title) => $"Seria {title}";

    private JsonArray FlatList()
    {
        var list = new JsonArray();
        for (ulong id = 1; id <= (ulong)_characters; id++)
        {
            list.Add(new JsonObject
            {
                ["c"] = id,
                ["p"] = id,
                ["f"] = "Postać",
                ["l"] = id.ToString(),
                ["t"] = TitleName(TitleOf(id)),
                ["n"] = id,
            });
        }
        return list;
    }

    private JsonObject Character(ulong id)
    {
        var title = TitleOf(id);
        return new JsonObject
        {
            ["character_id"] = id.ToString(),
            ["first_name"] = "Postać",
            ["last_name"] = id.ToString(),
            ["is_real"] = "0",
            ["picture_artifact_id"] = id.ToString(),
            ["gender"] = id % 2 == 0 ? "female" : "male",
            ["age"] = (16 + id % 20).ToString(),
            ["bio"] = new JsonObject { ["character_id"] = id.ToString(), ["biography"] = $"Opis postaci {id} z serii {TitleName(title)}.", ["lang"] = "pl" },
            ["points"] = new JsonArray(),
            ["pictures"] = new JsonArray(),
            ["relations"] = new JsonArray(new JsonObject
            {
                ["many_id"] = id.ToString(),
                ["title_id"] = title.ToString(),
                ["character_id"] = id.ToString(),
                ["title"] = TitleName(title),
                ["role"] = "main",
            }),
        };
    }

    private JsonObject TitleCharacters(ulong title)
    {
        var relations = new JsonArray();
        if (title >= FirstTitle && title < FirstTitle + (ulong)_titles)
        {
            for (var id = title - FirstTitle; id <= (ulong)_characters; id += (ulong)_titles)
            {
                if (id != 0)
                    relations.Add(Relation(id, title));
            }
        }
        else
        {
            // titles of real cards from an imported database: a stable handful of characters per title
            for (ulong k = 0; k < 8; k++)
                relations.Add(Relation((title * 31 + k * 7919) % (ulong)_characters + 1, title));
        }
        return new JsonObject { ["relations"] = relations };
    }

    private static JsonObject Relation(ulong id, ulong title) => new()
    {
        ["many_id"] = id.ToString(),
        ["character_id"] = id.ToString(),
        ["first_name"] = "Postać",
        ["last_name"] = id.ToString(),
        ["title"] = TitleName(title),
        ["role"] = "main",
    };

    private static JsonObject TitleInfo(ulong title) => new()
    {
        ["title"] = new JsonObject
        {
            ["title_id"] = title.ToString(),
            ["type"] = "Anime",
            ["title"] = TitleName(title),
            ["title_status"] = "Finished Airing",
            ["cover_artifact_id"] = title.ToString(),
            ["rating_total_sum"] = "80",
            ["rating_total_cnt"] = "10",
            ["anime"] = new JsonObject { ["anime_type"] = "TV", ["episodes"] = "12", ["episode_time"] = "24" },
            ["title_other"] = new JsonArray(),
            ["tags"] = new JsonArray(),
        },
    };

    private static JsonObject User(ulong id) => new()
    {
        ["user_id"] = id.ToString(),
        ["name"] = $"user{id}",
        ["rank"] = "Użytkownik",
        ["avatar"] = "0",
        ["watched_cnt"] = (id % 3000).ToString(),
        ["chapters_cnt"] = (id % 900).ToString(),
        ["watched_status"] = new JsonObject { ["in progress"] = "3", ["completed"] = (id % 400).ToString(), ["dropped"] = "2", ["skip"] = "0", ["plan"] = "20", ["hold"] = "1" },
        ["readed_status"] = new JsonObject { ["completed"] = (id % 50).ToString() },
        ["watch_time"] = new JsonObject { ["y"] = "0", ["n"] = "3", ["d"] = "4", ["h"] = "5", ["m"] = "6" },
        ["mean_anime_score"] = new JsonObject { ["mean_score"] = "7.4", ["scores_cnt"] = "120" },
    };

    private JsonObject Season()
    {
        var titles = Enumerable.Range(0, 20).Select(x => FirstTitle + (ulong)x).ToList();
        var links = new JsonArray();
        var characters = new JsonArray();
        foreach (var title in titles)
        {
            for (var id = title - FirstTitle; id <= (ulong)Math.Min(_characters, 2000); id += (ulong)_titles)
            {
                if (id == 0) continue;
                characters.Add(id);
                links.Add(new JsonObject { ["character_id"] = id, ["title_id"] = title });
            }
        }
        return new JsonObject
        {
            ["season"] = new JsonObject { ["year"] = 2026, ["season"] = "fall" },
            ["updated_at"] = "2026-11-01 12:00:00",
            ["titles"] = new JsonArray(titles.Select(x => (JsonNode)x).ToArray()),
            ["characters"] = characters,
            ["character_titles"] = links,
        };
    }

    // noise keeps the JPEG size close to a real photo-like picture instead of a few hundred bytes
    private static byte[] CreateImage(int seed)
    {
        var random = new Random(seed);
        using var image = new Image<Rgb24>(225, 350);
        var baseColor = new Rgb24((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256));
        image.ProcessPixelRows(rows =>
        {
            for (int y = 0; y < rows.Height; y++)
            {
                var row = rows.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    int noise = random.Next(-40, 41);
                    row[x] = new Rgb24(Clamp(baseColor.R + y / 3 + noise), Clamp(baseColor.G + x / 2 + noise), Clamp(baseColor.B - y / 4 + noise));
                }
            }
        });
        using var stream = new MemoryStream();
        image.SaveAsJpeg(stream, new JpegEncoder { Quality = 85 });
        return stream.ToArray();
    }

    private static byte Clamp(int value) => (byte)Math.Clamp(value, 0, 255);
}
