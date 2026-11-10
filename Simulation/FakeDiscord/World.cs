using System.Text.Json.Nodes;

namespace Sanakan.Simulation.FakeDiscord;

public sealed class SimUser
{
    public ulong Id { get; init; }
    public string Name { get; init; }
    public bool IsBot { get; init; }
    // state sampled from the imported database: lifetime command count, some of the player's cards, items
    public double Weight { get; init; } = 1;
    public List<ulong> Cards { get; init; } = new();
    public List<ulong> ExpeditionCards { get; init; } = new();
    public int Items { get; init; }
}

public enum ChannelKind { Waifu, Command, Fight, Duel, Market, Trash, Chat }

public sealed class SimChannel
{
    public ulong Id { get; init; }
    public string Name { get; init; }
}

public static class Snowflake
{
    private const long DiscordEpoch = 1420070400000;
    private static long _sequence;

    public static ulong Next()
        => (ulong)((DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - DiscordEpoch) << 22) | (ulong)(Interlocked.Increment(ref _sequence) & 0x3FFFFF);
}

// One guild with text channels and players, the state the fake gateway and REST answer from.
public sealed class World
{
    public World(int users, IEnumerable<string> channels)
    {
        BotUser = new SimUser { Id = Snowflake.Next(), Name = "Sanakan", IsBot = true };
        GuildId = Snowflake.Next();
        BotRoleId = Snowflake.Next();
        Channels = channels.Select(x => new SimChannel { Id = Snowflake.Next(), Name = x }).ToList();
        Users = Enumerable.Range(1, users).Select(x => new SimUser { Id = Snowflake.Next(), Name = $"player{x}" }).ToList();
        WaifuChannel = Channels[Math.Min(1, Channels.Count - 1)];
        TrashChannel = Channels[Math.Min(2, Channels.Count - 1)];
        foreach (var kind in Enum.GetValues<ChannelKind>())
            ByKind[kind] = new List<SimChannel> { kind == ChannelKind.Chat ? Channels[0] : kind == ChannelKind.Trash ? TrashChannel : WaifuChannel };
    }

    public Dictionary<ChannelKind, List<SimChannel>> ByKind { get; } = new();

    public SimChannel Channel(ChannelKind kind, Random random)
    {
        var list = ByKind.TryGetValue(kind, out var found) && found.Count > 0 ? found : ByKind[ChannelKind.Waifu];
        return list[random.Next(list.Count)];
    }

    private static ulong[] Ids(JsonNode node) => node?.AsArray().Select(x => (ulong)x!).ToArray() ?? Array.Empty<ulong>();

    // the real guild from tools/mysql_dump_to_sqlite.py --world: its channels and roles, every player as a member
    private World(JsonNode file)
    {
        var guild = file["guilds"]![0]!;
        BotUser = new SimUser { Id = Snowflake.Next(), Name = "Sanakan", IsBot = true };
        GuildId = (ulong)guild["id"]!;
        BotRoleId = Snowflake.Next();
        Prefix = guild["prefix"]?.ToString();

        var channels = new Dictionary<ulong, string>();
        foreach (var (name, id) in guild["channels"]!.AsObject())
            channels.TryAdd((ulong)id!, name);
        foreach (var (name, ids) in guild["channel_lists"]!.AsObject())
            foreach (var id in ids!.AsArray())
                channels.TryAdd((ulong)id!, $"{name}{channels.Count}");
        // chat channels are not in the configuration, these two earn experience like ordinary channels
        foreach (var name in new[] { "czat-1", "czat-2" })
            channels.TryAdd(Snowflake.Next(), name);
        Channels = channels.Select(x => new SimChannel { Id = x.Key, Name = x.Value }).ToList();

        SimChannel[] Pick(params ulong[] ids) => ids.Where(x => x != 0).Select(x => Channels.First(c => c.Id == x)).ToArray();
        var lists = guild["channel_lists"]!;
        var named = guild["channels"]!;
        ByKind[ChannelKind.Waifu] = Pick(Ids(lists["WaifuCommandChannels"])).ToList();
        ByKind[ChannelKind.Command] = Pick(Ids(lists["CommandChannels"])).ToList();
        ByKind[ChannelKind.Fight] = Pick(Ids(lists["WaifuFightChannels"])).ToList();
        ByKind[ChannelKind.Duel] = Pick((ulong?)named["WaifuDuelChannel"] ?? 0).ToList();
        ByKind[ChannelKind.Market] = Pick((ulong?)named["WaifuMarketChannel"] ?? 0).ToList();
        ByKind[ChannelKind.Trash] = Pick((ulong?)named["WaifuTrashCommandsChannel"] ?? 0).ToList();
        ByKind[ChannelKind.Chat] = Channels.Where(x => x.Name.StartsWith("czat-")).ToList();

        var waifu = guild["channel_lists"]?["WaifuCommandChannels"]?[0];
        WaifuChannel = waifu != null ? Channels.First(x => x.Id == (ulong)waifu) : Channels[0];
        var trash = guild["channels"]?["WaifuTrashCommandsChannel"];
        TrashChannel = trash != null ? Channels.First(x => x.Id == (ulong)trash) : WaifuChannel;

        var roles = guild["roles"]!.AsObject().ToDictionary(x => x.Key, x => (ulong)x.Value!);
        foreach (var (_, ids) in guild["role_lists"]!.AsObject())
            foreach (var id in ids!.AsArray())
                roles.TryAdd($"role{roles.Count}", (ulong)id!);
        Roles = roles.Values.Distinct().ToList();
        MemberRoles = new[] { "UserRole", "WaifuRole" }.Where(roles.ContainsKey).Select(x => roles[x]).ToList();
        AdminRole = roles.TryGetValue("AdminRole", out var admin) ? admin : 0;

        // most active players first, so low indexes have cards and history
        Users = file["users"]!.AsArray().Select((x, i) => new SimUser
        {
            Id = (ulong)x!["id"]!,
            Name = $"player{i + 1}",
            Weight = (double?)x["commands"] ?? 0,
            Cards = Ids(x["cards"]).ToList(),
            ExpeditionCards = Ids(x["expedition_cards"]).ToList(),
            Items = (int?)x["items"] ?? 0,
        }).ToList();
    }

    public static World FromFile(string path) => new(JsonNode.Parse(File.ReadAllText(path)));

    public SimUser BotUser { get; }
    public ulong GuildId { get; }
    public ulong BotRoleId { get; }
    public List<SimChannel> Channels { get; }
    public List<SimUser> Users { get; }
    public SimChannel WaifuChannel { get; }
    public SimChannel TrashChannel { get; }
    public string Prefix { get; }
    public List<ulong> Roles { get; } = new();
    public List<ulong> MemberRoles { get; } = new();
    public ulong AdminRole { get; }

    // roles the bot gave or took through REST (mutes, level roles), on top of the defaults
    private readonly System.Collections.Concurrent.ConcurrentDictionary<ulong, (HashSet<ulong> Added, HashSet<ulong> Removed)> _roleChanges = new();

    public void ChangeRole(ulong userId, ulong roleId, bool add)
    {
        var changes = _roleChanges.GetOrAdd(userId, _ => (new HashSet<ulong>(), new HashSet<ulong>()));
        lock (changes.Added)
        {
            (add ? changes.Added : changes.Removed).Add(roleId);
            (add ? changes.Removed : changes.Added).Remove(roleId);
        }
    }

    private JsonArray RolesOf(SimUser user)
    {
        if (user.IsBot) return new JsonArray(BotRoleId.ToString());
        var roles = MemberRoles.ToList();
        if (AdminRole != 0 && user == Users[0]) roles.Add(AdminRole);
        if (_roleChanges.TryGetValue(user.Id, out var changes))
        {
            lock (changes.Added)
                roles = roles.Concat(changes.Added).Where(x => !changes.Removed.Contains(x)).Distinct().ToList();
        }
        return new JsonArray(roles.Select(x => (JsonNode)x.ToString()).ToArray());
    }
    public DateTimeOffset Created { get; } = DateTimeOffset.UtcNow.AddDays(-30);

    private Dictionary<ulong, SimUser> _usersById;

    public SimUser FindUser(ulong id)
    {
        _usersById ??= Users.Append(BotUser).GroupBy(x => x.Id).ToDictionary(x => x.Key, x => x.First());
        return _usersById.GetValueOrDefault(id);
    }

    public static string Time(DateTimeOffset time) => time.ToString("yyyy-MM-dd'T'HH:mm:ss.fffzzz");

    public JsonObject UserJson(SimUser user) => new()
    {
        ["id"] = user.Id.ToString(),
        ["username"] = user.Name,
        ["discriminator"] = "0",
        ["global_name"] = user.Name,
        ["avatar"] = null,
        ["bot"] = user.IsBot,
        ["public_flags"] = 0,
    };

    public JsonObject MemberJson(SimUser user, bool withUser = true)
    {
        var member = new JsonObject
        {
            ["nick"] = null,
            ["avatar"] = null,
            ["roles"] = RolesOf(user),
            ["joined_at"] = Time(Created),
            ["deaf"] = false,
            ["mute"] = false,
            ["flags"] = 0,
            ["pending"] = false,
        };
        if (withUser)
            member["user"] = UserJson(user);
        return member;
    }

    public JsonObject ChannelJson(SimChannel channel, int position) => new()
    {
        ["id"] = channel.Id.ToString(),
        ["type"] = 0,
        ["guild_id"] = GuildId.ToString(),
        ["name"] = channel.Name,
        ["position"] = position,
        ["permission_overwrites"] = new JsonArray(),
        ["topic"] = null,
        ["nsfw"] = false,
        ["last_message_id"] = null,
        ["rate_limit_per_user"] = 0,
        ["parent_id"] = null,
    };

    private static JsonObject RoleJson(ulong id, string name, string permissions, int position) => new()
    {
        ["id"] = id.ToString(),
        ["name"] = name,
        ["color"] = 0,
        ["hoist"] = false,
        ["icon"] = null,
        ["unicode_emoji"] = null,
        ["position"] = position,
        ["permissions"] = permissions,
        ["managed"] = false,
        ["mentionable"] = false,
        ["flags"] = 0,
    };

    public JsonObject GuildCreateJson(int membersInline)
    {
        var members = new JsonArray(MemberJson(BotUser));
        foreach (var user in Users.Take(membersInline))
            members.Add(MemberJson(user));

        return new JsonObject
        {
            ["id"] = GuildId.ToString(),
            ["name"] = "Sanakan simulation",
            ["icon"] = null,
            ["splash"] = null,
            ["discovery_splash"] = null,
            ["owner_id"] = Users[0].Id.ToString(),
            ["afk_channel_id"] = null,
            ["afk_timeout"] = 300,
            ["verification_level"] = 0,
            ["default_message_notifications"] = 0,
            ["explicit_content_filter"] = 0,
            ["roles"] = new JsonArray(new[] { RoleJson(GuildId, "@everyone", "104324673", 0), RoleJson(BotRoleId, "Sanakan", "8", 1) }
                .Concat(Roles.Where(x => x != GuildId).Select((x, i) => RoleJson(x, $"role{i}", "0", i + 2))).Select(x => (JsonNode)x).ToArray()),
            ["emojis"] = new JsonArray(),
            ["stickers"] = new JsonArray(),
            ["features"] = new JsonArray(),
            ["mfa_level"] = 0,
            ["application_id"] = null,
            ["system_channel_id"] = null,
            ["system_channel_flags"] = 0,
            ["rules_channel_id"] = null,
            ["max_members"] = 500000,
            ["vanity_url_code"] = null,
            ["description"] = null,
            ["banner"] = null,
            ["premium_tier"] = 0,
            ["premium_subscription_count"] = 0,
            ["preferred_locale"] = "pl",
            ["public_updates_channel_id"] = null,
            ["nsfw_level"] = 0,
            ["premium_progress_bar_enabled"] = false,
            ["joined_at"] = Time(Created),
            ["large"] = Users.Count > membersInline,
            ["unavailable"] = false,
            ["member_count"] = Users.Count + 1,
            ["voice_states"] = new JsonArray(),
            ["members"] = members,
            ["channels"] = new JsonArray(Channels.Select((x, i) => (JsonNode)ChannelJson(x, i)).ToArray()),
            ["threads"] = new JsonArray(),
            ["presences"] = new JsonArray(),
            ["stage_instances"] = new JsonArray(),
            ["guild_scheduled_events"] = new JsonArray(),
        };
    }

    public JsonObject MessageJson(ulong id, ulong channelId, SimUser author, string content, JsonArray embeds = null,
        JsonArray attachments = null, JsonArray components = null, DateTimeOffset? edited = null)
    {
        var message = new JsonObject
        {
            ["id"] = id.ToString(),
            ["channel_id"] = channelId.ToString(),
            ["guild_id"] = GuildId.ToString(),
            ["author"] = UserJson(author),
            ["member"] = MemberJson(author, withUser: false),
            ["content"] = content ?? "",
            ["timestamp"] = Time(DateTimeOffset.UtcNow),
            ["edited_timestamp"] = edited.HasValue ? Time(edited.Value) : null,
            ["tts"] = false,
            ["mention_everyone"] = false,
            ["mentions"] = new JsonArray(),
            ["mention_roles"] = new JsonArray(),
            ["attachments"] = attachments ?? new JsonArray(),
            ["embeds"] = embeds ?? new JsonArray(),
            ["components"] = components ?? new JsonArray(),
            ["pinned"] = false,
            ["type"] = 0,
            ["flags"] = 0,
        };
        return message;
    }
}
