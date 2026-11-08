#pragma warning disable 1591

using System.Collections.Generic;
using Discord;
using Sanakan.Api;
using Sanakan.Api.Models;
using Sanakan.Extensions;

namespace Sanakan.Services
{
    public static class BotInfoFormat
    {
        // the version may carry a "+commit" suffix, the site expects the plain number
        public static string ChangesUrl(string version)
            => $"https://sanakan.pl/state/versions/?v={System.Uri.EscapeDataString((version ?? "").Split('+')[0])}";

        private const int HighLatencyMs = 1000;

        public static string Status(HealthStatus health)
        {
            if (health == null || health.Status == HealthMonitor.Ok)
                return "✅ Wszystko działa.";

            var problems = new List<string>();
            if (health.Shinden != null && !health.Shinden.Ok) problems.Add("Shinden nie odpowiada");
            if (health.Database != null && !health.Database.Ok) problems.Add("baza danych ma problemy");
            if (health.Discord != null && health.Discord.LatencyMs > HighLatencyMs) problems.Add("Discord odpowiada z dużym opóźnieniem");
            if (health.Commands != null && health.Commands.Rejected5Min > 0) problems.Add("bot jest przeciążony");

            if (problems.Count == 0)
                return "⚠️ Bot działa z problemami.";

            var text = string.Join(", ", problems);
            return $"⚠️ {char.ToUpperInvariant(text[0])}{text.Substring(1)}.";
        }

        public static Embed Build(HealthStatus health, string prefix)
        {
            var embed = new EmbedBuilder
            {
                Title = "🐙 Sanakan",
                Color = EMType.Bot.Color(),
                Description = Status(health),
                Footer = new EmbedFooterBuilder().WithText($"Prefiks tego serwera: {prefix} · lista poleceń: {prefix}pomoc"),
            }
            .AddField("Wersja", health?.Version ?? HealthMonitor.GetVersion(), true);

            if (health != null)
            {
                embed.AddField("Działa od", health.StartedAt.ToRemTime(), true)
                    .AddField("Opóźnienie", $"{health.Discord?.LatencyMs ?? 0} ms", true)
                    .AddField("Serwery", $"{health.Discord?.Guilds ?? 0}", true);
            }

            return embed.Build();
        }

        public static MessageComponent Links(string version) => new ComponentBuilder()
            .WithButton("Strona", style: ButtonStyle.Link, url: "https://sanakan.pl/")
            .WithButton("Wiki", style: ButtonStyle.Link, url: "https://wiki.sanakan.pl/")
            .WithButton("Alter", style: ButtonStyle.Link, url: "https://alter.sanakan.pl/")
            .WithButton("GitHub", style: ButtonStyle.Link, url: "https://github.com/MZKNEK/sanakan.git")
            .WithButton("Zmiany w tej wersji", style: ButtonStyle.Link, url: ChangesUrl(version), row: 1)
            .Build();
    }
}
