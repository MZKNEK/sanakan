using Sanakan.Simulation.FakeDiscord;

namespace Sanakan.Simulation;

// The bot's web API as the site and the players call it. MixKey is the route as the traffic mix names it (hourly API
// reports or the logged writes), its weight there drives --api; every template can be forced in with --extra api:name.
public static class ApiCatalog
{
    public enum Auth { Player, Site, Info }

    // GivesPacks/NeedsPacks: opening goes to players the simulation gave packs to, most random players have none
    public sealed record Template(string MixKey, HttpMethod Method, Auth Auth, Func<Context, string> Path, string Body = null, bool NeedsShinden = false,
        bool GivesPacks = false, bool NeedsPacks = false);

    public sealed class Context
    {
        public SimUser User { get; init; }
        public ulong Shinden { get; init; }
        public Random Random { get; init; }
    }

    private const string Pack = "[{\"name\":\"Symulacja\",\"count\":1,\"pool\":{\"type\":\"Random\"}}]";

    private static Template Player(string path) => new($"GET api/waifu/{path}", HttpMethod.Get, Auth.Player, _ => $"api/waifu/{path}");

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
            _ => "api/Waifu/boosterpack/open/1", NeedsPacks: true),
        ["api:give-packs"] = new("POST /api/waifu/discord/{n}/boosterpack", HttpMethod.Post, Auth.Site,
            c => $"api/Waifu/discord/{c.User.Id}/boosterpack", Pack, GivesPacks: true),
        ["api:give-packs-shinden"] = new("POST /api/waifu/shinden/{n}/boosterpack", HttpMethod.Post, Auth.Site,
            c => $"api/Waifu/shinden/{c.Shinden}/boosterpack", Pack, NeedsShinden: true),
        ["api:user"] = new("GET api/User/discord/{id}", HttpMethod.Get, Auth.Site, c => $"api/User/discord/{c.User.Id}"),
        ["api:user-shinden"] = new("GET api/User/shinden/{id}", HttpMethod.Get, Auth.Site, c => $"api/User/shinden/{c.Shinden}", NeedsShinden: true),
        ["api:profile-preview"] = new("GET api/User/discord/{id}/profile", HttpMethod.Get, Auth.Info, c => $"api/User/discord/{c.User.Id}/profile"),
    };
}
