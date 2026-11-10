using Sanakan.Simulation.FakeDiscord;

namespace Sanakan.Simulation;

// How to send each command the production logs show: the channel its precondition wants and plausible arguments
// built from the player's sampled state. Commands missing here are left out of the mix.
public static class CommandCatalog
{
    public sealed record Template(ChannelKind Channel, Func<Context, string> Text);

    public sealed class Context
    {
        public SimUser User { get; init; }
        public World World { get; init; }
        public Random Random { get; init; }

        public string Card => User.Cards.Count > 0 ? User.Cards[Random.Next(User.Cards.Count)].ToString() : Random.Next(1, 900000).ToString();
        public string ExpeditionCard => User.ExpeditionCards.Count > 0 ? User.ExpeditionCards[Random.Next(User.ExpeditionCards.Count)].ToString() : Card;
        public string Item => Random.Next(1, Math.Max(1, Math.Min(User.Items, 30)) + 1).ToString();
        public string OtherPlayer => $"<@{World.Users[Random.Next(Math.Min(300, World.Users.Count))].Id}>";
        public string Character => Random.Next(1, 20001).ToString();
    }

    private static Template T(ChannelKind channel, Func<Context, string> text) => new(channel, text);
    private static Template T(ChannelKind channel, string text) => new(channel, _ => text);

    public static readonly IReadOnlyDictionary<string, Template> Templates = new Dictionary<string, Template>
    {
        ["użyj"] = T(ChannelKind.Waifu, c => $"użyj {c.Item} {c.Card}"),
        ["wyprawa koniec"] = T(ChannelKind.Fight, c => $"wyprawa koniec {c.ExpeditionCard}"),
        ["pojedynek"] = T(ChannelKind.Duel, "pojedynek"),
        ["karta+"] = T(ChannelKind.Command, "karta+"),
        ["ulepsz"] = T(ChannelKind.Waifu, c => $"ulepsz {c.Card}"),
        ["czarny rynek"] = T(ChannelKind.Waifu, c => $"czarny rynek {c.Card}"),
        ["zaskórniaki"] = T(ChannelKind.Command, "zaskórniaki"),
        ["wyprawa"] = T(ChannelKind.Fight, c => $"wyprawa n {c.Card}"),
        ["rynek"] = T(ChannelKind.Waifu, c => $"rynek {c.Card}"),
        ["oznacz"] = T(ChannelKind.Waifu, c => $"oznacz Wymiana {c.Card} {c.Card}"),
        ["drobne"] = T(ChannelKind.Command, "drobne"),
        ["misje"] = T(ChannelKind.Command, "misje"),
        ["pakiet"] = T(ChannelKind.Waifu, c => c.Random.Next(2) == 0 ? "pakiet" : "pakiet 1"),
        ["druciarstwo"] = T(ChannelKind.Waifu, "druciarstwo 1"),
        ["przedmioty"] = T(ChannelKind.Waifu, "przedmioty"),
        ["ofiaruj"] = T(ChannelKind.Waifu, c => $"ofiaruj {c.Card}"),
        ["kto chce"] = T(ChannelKind.Waifu, c => $"kto chce {c.Card}"),
        ["waifu"] = T(ChannelKind.Waifu, c => $"waifu {c.Card}"),
        ["użyjbk"] = T(ChannelKind.Waifu, c => $"użyjbk {c.Item} 1"),
        ["zniszcz"] = T(ChannelKind.Waifu, c => $"zniszcz {c.Card}"),
        ["uwolnij"] = T(ChannelKind.Waifu, c => $"uwolnij {c.Card}"),
        ["loteria"] = T(ChannelKind.Waifu, "loteria 1"),
        ["talia"] = T(ChannelKind.Waifu, "talia"),
        ["klatka"] = T(ChannelKind.Waifu, "klatka"),
        ["wyprawa status"] = T(ChannelKind.Fight, "wyprawa status"),
        ["karcianka"] = T(ChannelKind.Waifu, "karcianka"),
        ["życzenia filtr"] = T(ChannelKind.Waifu, c => $"życzenia filtr {c.OtherPlayer}"),
        ["karta"] = T(ChannelKind.Waifu, c => $"karta {c.Card}"),
        ["życzenia"] = T(ChannelKind.Waifu, "życzenia"),
        ["oznacz puste"] = T(ChannelKind.Waifu, "oznacz puste Wymiana"),
        ["rzut"] = T(ChannelKind.Command, c => $"rzut {(c.Random.Next(2) == 0 ? "orzeł" : "reszka")} {c.Random.Next(1, 21)}"),
        ["wymiana"] = T(ChannelKind.Market, c => $"wymiana {c.OtherPlayer}"),
        ["barter"] = T(ChannelKind.Waifu, c => $"barter {c.Item}:1"),
        ["strona"] = T(ChannelKind.Command, "strona"),
        ["portfel"] = T(ChannelKind.Command, "portfel"),
        ["profil"] = T(ChannelKind.Command, "profil"),
        ["karta obrazek"] = T(ChannelKind.Command, c => $"karta obrazek {c.Card}"),
        ["żdodaj"] = T(ChannelKind.Waifu, c => $"żdodaj postać {c.Character}"),
        ["karcianka-"] = T(ChannelKind.Command, "karcianka-"),
        ["poświęć"] = T(ChannelKind.Waifu, c => $"poświęć {c.Card} {c.Card}"),
        ["wyzwól"] = T(ChannelKind.Waifu, c => $"wyzwól {c.Card}"),
        ["koszary"] = T(ChannelKind.Waifu, "koszary"),
        ["pomoc"] = T(ChannelKind.Command, "pomoc"),
        ["idp"] = T(ChannelKind.Command, "idp"),
    };
}
