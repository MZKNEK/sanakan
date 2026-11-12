using Sanakan.Simulation.FakeDiscord;

namespace Sanakan.Simulation;

// The bot's web API as the site and the players call it. MixKey is the route as the traffic mix names it (hourly API
// reports or the logged writes), its weight there drives --api; every template can be forced in with --extra api:name.
public static class ApiCatalog
{
    public enum Auth { Player, Site, Info }

    // GivesPacks/NeedsPacks: opening goes to players the simulation gave packs to, most random players have none;
    // Idempotent: the request carries a new Idempotency-Key, which these routes require
    public sealed record Template(string MixKey, HttpMethod Method, Auth Auth, Func<Context, string> Path, Func<Context, string> Body = null, bool NeedsShinden = false,
        bool GivesPacks = false, bool NeedsPacks = false, bool Idempotent = false);

    public sealed class Context
    {
        public SimUser User { get; init; }
        public ulong Shinden { get; init; }
        public Random Random { get; init; }
    }

    private static readonly Func<Context, string> Pack = _ => "[{\"name\":\"Symulacja\",\"count\":1,\"pool\":{\"type\":\"Random\"}}]";

    // the sorts players use most (picture, power, id, character, fatigue, curse) and a few others, sometimes with a
    // default tag (1 favourite, 2 gallery, 4 exchange) or a text; reverse direction half of the time
    private static readonly string[] Sorts = { "picture", "picture", "cardPower", "id", "characterId", "fatigue", "curse", "rarity", "relation", "title" };

    private static string CardFilter(Context c, bool text)
    {
        var sort = Sorts[c.Random.Next(Sorts.Length)] + (c.Random.Next(2) == 0 ? "Des" : "");
        var tag = c.Random.Next(4) == 0 ? $"[{{\"id\":{new[] { 1, 2, 4 }[c.Random.Next(3)]},\"name\":\"x\"}}]" : "null";
        var search = text && c.Random.Next(3) == 0 ? $"\"{new[] { "rem", "naruto", "a", "zzqx" }[c.Random.Next(4)]}\"" : "null";
        return $"{{\"orderBy\":\"{sort}\",\"searchText\":{search},\"includeTags\":{tag},\"filterTagsMethod\":\"and\"}}";
    }

    // only for --extra: a MixKey the traffic mix does not have, so the route's mix rate stays with api:total-cards
    private static Template RarityPage(bool search) => new($"extra total cards by rarity{(search ? " with search" : "")}", HttpMethod.Post, Auth.Site,
        c => $"api/Waifu/total/cards/{(c.Random.Next(4) == 0 ? 500 : 0)}/50", c =>
        {
            var sort = c.Random.Next(2) == 0 ? "rarity" : "rarityDes";
            var text = search ? $"\"{new[] { "rem", "naruto", "zzqx" }[c.Random.Next(3)]}\"" : "null";
            return $"{{\"orderBy\":\"{sort}\",\"searchText\":{text},\"includeTags\":null,\"filterTagsMethod\":\"and\"}}";
        });

    private static Template Player(string path) => new($"GET api/waifu/{path}", HttpMethod.Get, Auth.Player, _ => $"api/waifu/{path}");

    // the site asks for the image of a card it shows; a player without sampled cards asks for a card that does not exist
    private static string CardImage(Context c)
    {
        var id = c.User.Cards.Count > 0 ? c.User.Cards[c.Random.Next(c.User.Cards.Count)] : 1;
        var type = new[] { "", "", "?type=profile", "?type=small" }[c.Random.Next(4)];
        return $"api/Waifu/card/{id}{type}";
    }

    public static readonly IReadOnlyDictionary<string, Template> Templates = new Dictionary<string, Template>
    {
        ["api:limits"] = Player("limits"),
        ["api:missions"] = Player("missions"),
        ["api:items"] = Player("items"),
        ["api:deck"] = Player("deck"),
        ["api:figures"] = Player("figures"),
        ["api:boosterpacks"] = Player("boosterpacks"),
        ["api:wishlist"] = Player("wishlist"),
        ["api:tags"] = Player("tags"),
        ["api:pack-open"] = new("POST api/Waifu/boosterpack/open/{packNumber}", HttpMethod.Post, Auth.Player,
            _ => "api/Waifu/boosterpack/open/1", NeedsPacks: true, Idempotent: true),
        ["api:give-packs"] = new("POST /api/waifu/discord/{n}/boosterpack", HttpMethod.Post, Auth.Site,
            c => $"api/Waifu/discord/{c.User.Id}/boosterpack", Pack, GivesPacks: true, Idempotent: true),
        ["api:give-packs-shinden"] = new("POST /api/waifu/shinden/{n}/boosterpack", HttpMethod.Post, Auth.Site,
            c => $"api/Waifu/shinden/{c.Shinden}/boosterpack", Pack, NeedsShinden: true, Idempotent: true),
        // the site opens the packs it grants in the background and picks the result up under the key
        ["api:open-packs-shinden"] = new("POST /api/waifu/shinden/{n}/boosterpack/open", HttpMethod.Post, Auth.Site,
            c => $"api/Waifu/shinden/{c.Shinden}/boosterpack/open", Pack, NeedsShinden: true, Idempotent: true),
        ["api:tc-shinden"] = new("PUT /api/user/shinden/{n}/tc", HttpMethod.Put, Auth.Site,
            c => $"api/User/shinden/{c.Shinden}/tc", _ => "1", NeedsShinden: true, Idempotent: true),
        // only for --extra: a new route the traffic mix does not have yet
        ["api:deck-card"] = new("extra set card active", HttpMethod.Put, Auth.Player,
            c => $"api/Waifu/deck/card/{(c.User.Cards.Count > 0 ? c.User.Cards[c.Random.Next(c.User.Cards.Count)] : 1)}",
            c => c.Random.Next(2) == 0 ? "true" : "false"),
        ["api:card-image"] = new("GET api/Waifu/card/{id}", HttpMethod.Get, Auth.Site, CardImage),
        ["api:user"] = new("GET api/User/discord/{id}", HttpMethod.Get, Auth.Site, c => $"api/User/discord/{c.User.Id}"),
        ["api:user-shinden"] = new("GET api/User/shinden/{id}", HttpMethod.Get, Auth.Site, c => $"api/User/shinden/{c.Shinden}", NeedsShinden: true),
        ["api:user-cards"] = new("POST api/Waifu/user/{id}/cards/{offset}/{count}", HttpMethod.Post, Auth.Site,
            c => $"api/Waifu/user/{c.Shinden}/cards/{(c.Random.Next(4) == 0 ? 50 : 0)}/50", c => CardFilter(c, false), NeedsShinden: true),
        ["api:total-cards"] = new("POST api/Waifu/total/cards/{offset}/{count}", HttpMethod.Post, Auth.Site,
            c => $"api/Waifu/total/cards/{(c.Random.Next(4) == 0 ? 500 : 0)}/50", c => CardFilter(c, true)),
        // the rarity sort over all cards has its own index, a text search has to keep off it
        ["api:total-cards-rarity"] = RarityPage(false),
        ["api:total-cards-rarity-search"] = RarityPage(true),
        // they end the bot, only for checking its exit code (ExitCodes.Update, ExitCodes.Stop)
        ["api:debug-update"] = new("POST api/Debug/update", HttpMethod.Post, Auth.Site, _ => "api/Debug/update"),
        ["api:debug-kill"] = new("POST api/Debug/kill", HttpMethod.Post, Auth.Site, _ => "api/Debug/kill"),
        ["api:profile-preview"] = new("GET api/User/discord/{id}/profile", HttpMethod.Get, Auth.Info, c => $"api/User/discord/{c.User.Id}/profile"),
    };
}
