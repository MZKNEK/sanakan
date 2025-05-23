﻿#pragma warning disable 1591

using System;
using System.Linq;
using System.Threading.Tasks;
using Discord.Commands;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using Sanakan.Extensions;
using Sanakan.Preconditions;
using Sanakan.Services.Commands;
using Sanakan.Services.Session;
using Sanakan.Services.Session.Models;
using Sanakan.Services.Time;
using Z.EntityFramework.Plus;
using Shden = Shinden;

namespace Sanakan.Modules
{
    [Name("Shinden"), RequireUserRole]
    public class Shinden : SanakanModuleBase<SocketCommandContext>
    {
        private Shden.ShindenClient _shclient;
        private Services.Shinden _shinden;
        private SessionManager _session;
        private ISystemTime _time;

        public Shinden(Shden.ShindenClient client, SessionManager session, Services.Shinden shinden, ISystemTime time)
        {
            _shclient = client;
            _session = session;
            _shinden = shinden;
            _time = time;
        }

        [Command("odcinki", RunMode = RunMode.Async)]
        [Alias("episodes")]
        [Summary("wyświetla nowo dodane epizody")]
        [Remarks(""), RequireCommandChannel]
        public async Task ShowNewEpisodesAsync()
        {
            var response = await _shclient.GetNewEpisodesAsync();
            if (response.IsSuccessStatusCode())
            {
                var episodes = response.Body;
                if (episodes?.Count > 0)
                {
                    var msg = await SafeReplyAsync("", embed: "Lista poszła na PW!".ToEmbedMessage(EMType.Success).Build());

                    try
                    {
                        var dm = await Context.User.CreateDMChannelAsync();
                        foreach (var ep in episodes)
                        {
                            await dm.SendMessageAsync("", false, ep.ToEmbed());
                            await Task.Delay(500);
                        }
                        await dm.CloseAsync();
                    }
                    catch (Exception ex)
                    {
                        await msg.ModifyAsync(x => x.Embed = $"{Context.User.Mention} nie udało się wysłać PW! ({ex.Message})".ToEmbedMessage(EMType.Error).Build());
                    }

                    return;
                }
            }

            await SafeReplyAsync("", embed: "Nie udało się pobrać listy odcinków.".ToEmbedMessage(EMType.Error).Build());
        }

        [Command("anime", RunMode = RunMode.Async)]
        [Alias("bajka")]
        [Summary("wyświetla informacje o anime")]
        [Remarks("Soul Eater")]
        public async Task SearchAnimeAsync([Summary("tytuł")][Remainder]string title)
        {
            await _shinden.SendSearchInfoAsync(Context, title, Shden.QuickSearchType.Anime);
        }

        [Command("manga", RunMode = RunMode.Async)]
        [Alias("komiks")]
        [Summary("wyświetla informacje o mandze")]
        [Remarks("Gintama")]
        public async Task SearchMangaAsync([Summary("tytuł")][Remainder]string title)
        {
            await _shinden.SendSearchInfoAsync(Context, title, Shden.QuickSearchType.Manga);
        }

        [Command("postać", RunMode = RunMode.Async)]
        [Alias("postac", "character")]
        [Summary("wyświetla informacje o postaci")]
        [Remarks("Gintoki")]
        public async Task SearchCharacterBasicAsync([Summary("imię")][Remainder]string name) => await SearchCharacterAsync(false, name);

        [Command("postać", RunMode = RunMode.Async)]
        [Alias("postac", "character")]
        [Summary("wyświetla informacje o postaci")]
        [Remarks("Gintoki")]
        public async Task SearchCharacterAsync([Summary("czy szukać tytułów?")]bool longSearch, [Summary("imię")][Remainder]string name)
        {
            var session = new SearchSession(Context.User, _shclient, _shinden);
            if (_session.SessionExist(session)) return;

            var response = await _shclient.Search.CharacterAsync(name);
            if (!response.IsSuccessStatusCode())
            {
                await SafeReplyAsync("", embed: _shinden.GetResponseFromSearchCode(response).ToEmbedMessage(EMType.Error).Build());
                return;
            }

            var list = response.Body;
            if (list.Count == 1)
            {
                var info = await _shinden.GetCharacterInfoAsync(list.First().Id);
                await SafeReplyAsync("", false, info.ToEmbed());
                return;
            }

            System.Collections.Generic.IEnumerable<object> eList = list;
            Discord.IUserMessage buildingMsg = null;
            if (longSearch)
            {
                if (list.Count > 6)
                    buildingMsg = await SafeReplyAsync("", embed: $"📚 Przeglądanie zakurzonych półek...".ToEmbedMessage(EMType.Bot).Build());

                eList = await _shinden.GetTitlesForCharactersAsync(list);
            }

            var toSend = _shinden.GetSearchResponse(eList, "Wybierz postać, którą chcesz wyświetlić poprzez wpisanie numeru odpowiadającemu jej na liście.");

            if (buildingMsg != null)
                await buildingMsg.DeleteAsync();

            session.PList = list;
            await _shinden.SendSearchResponseAsync(Context, toSend, session);
        }

        [Command("strona", RunMode = RunMode.Async)]
        [Alias("ile", "otaku", "site", "mangozjeb")]
        [Summary("wyświetla statystyki użytkownika z strony")]
        [Remarks("Karna"), DelayNextUseBy(30)]
        public async Task ShowSiteStatisticAsync([Summary("nazwa użytkownika")]SocketGuildUser user = null)
        {
            var usr = user ?? Context.User as SocketGuildUser;
            if (usr == null) return;

            using (var db = new Database.DatabaseContext(Config))
            {
                var botUser = await db.GetCachedFullUserAsync(usr.Id);
                if (botUser == null)
                {
                    await SafeReplyAsync("", embed: "Ta osoba nie ma profilu bota.".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                if (botUser?.Shinden == 0)
                {
                    await SafeReplyAsync("", embed: "Ta osoba nie połączyła konta bota z kontem na stronie.".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                using (var stream = await _shinden.GetSiteStatisticAsync(botUser.Shinden, usr))
                {
                    if (stream == null)
                    {
                        await SafeReplyAsync("", embed: $"Brak połączenia z Shindenem!".ToEmbedMessage(EMType.Error).Build());
                        return;
                    }

                    await Context.Channel.SendFileAsync(stream, $"{usr.Id}.png", $"<{Shden.API.Url.GetProfileURL(botUser.Shinden)}>");
                }
            }
        }

        [Command("połącz")]
        [Alias("connect", "polacz", "połacz", "polącz")]
        [Summary("łączy funkcje bota, z kontem na stronie")]
        [Remarks("https://shinden.pl/user/136-mo0nisi44")]
        public async Task ConnectAsync([Summary("adres do profilu")]string url)
        {
            switch (_shinden.ParseUrlToShindenId(url, out var shindenId))
            {
                case Services.UrlParsingError.InvalidUrl:
                    await SafeReplyAsync("", embed: "Wygląda na to, że podałeś niepoprawny link.".ToEmbedMessage(EMType.Error).Build());
                    return;

                case Services.UrlParsingError.InvalidUrlForum:
                await SafeReplyAsync("", embed: "Wygląda na to, że podałeś link do forum zamiast strony.".ToEmbedMessage(EMType.Error).Build());
                    return;

                default:
                case Services.UrlParsingError.None:
                    break;
            }

            var response = await _shclient.User.GetAsync(shindenId);
            if (response.IsSuccessStatusCode())
            {
                var user = response.Body;
                var userNameInDiscord = Context.User.GetUserNickInGuild();

                if (!user.Name.Equals(userNameInDiscord))
                {
                    await SafeReplyAsync("", embed: "Wykryto próbę podszycia się. Nieładnie!".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                using (var db = new Database.DatabaseContext(Config))
                {
                    var anyUser = await db.Users.AsQueryable().AsNoTracking().FirstOrDefaultAsync(x => x.Shinden == shindenId);
                    if (anyUser != null)
                    {
                        if (anyUser.Id == Context.User.Id)
                        {
                            await SafeReplyAsync("", embed: "Jesteś już połączony z tym kontem.".ToEmbedMessage(EMType.Info).Build());
                        }
                        else
                        {
                            await SafeReplyAsync("", embed: $"Wygląda na to, że ktoś już połączył się z tym kontem: <@{anyUser.Id}>".ToEmbedMessage(EMType.Error).Build());
                        }
                        return;
                    }

                    var botuser = await db.GetUserOrCreateSimpleAsync(Context.User.Id);
                    botuser.Shinden = shindenId;

                    await db.UserActivities.AddAsync(new Services.UserActivityBuilder(_time)
                        .WithUser(botuser, userNameInDiscord).WithType(Database.Models.ActivityType.Connected).Build());

                    await db.SaveChangesAsync();

                    QueryCacheManager.ExpireTag(new string[] { $"user-{botuser.Id}" });
                }

                await SafeReplyAsync("", embed: "Konta zostały połączone.".ToEmbedMessage(EMType.Success).Build());
                return;
            }

            await SafeReplyAsync("", embed: $"Brak połączenia z Shindenem! ({response.Code})".ToEmbedMessage(EMType.Error).Build());
        }
    }
}
