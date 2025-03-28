﻿#pragma warning disable 1591

using Discord.Commands;
using Sanakan.Services;
using Sanakan.Extensions;
using Sanakan.Services.Commands;
using System.Threading.Tasks;
using Sanakan.Preconditions;
using Discord.WebSocket;
using System.Linq;
using System;

namespace Sanakan.Modules
{
    [Name("Kraina"), RequireUserRole]
    public class Lands : SanakanModuleBase<SocketCommandContext>
    {
        private LandManager _manager;

        public Lands(LandManager manager)
        {
            _manager = manager;
        }

        [Command("ludność", RunMode = RunMode.Async)]
        [Alias("ludnosc", "ludnośc", "ludnosć", "people")]
        [Summary("wyświetla użytkowników należących do krainy")]
        [Remarks("Kotleciki")]
        public async Task ShowPeopleAsync([Summary("nazwa krainy")][Remainder]string name = null)
        {
            using (var db = new Database.DatabaseContext(Config))
            {
                var config = await db.GetCachedGuildFullConfigAsync(Context.Guild.Id);
                var land = _manager.DetermineLand(config.Lands, Context.User as SocketGuildUser, name);
                if (land == null)
                {
                    await SafeReplyAsync("", embed: "Nie zarządzasz żadną krainą.".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                foreach (var emb in _manager.GetMembersList(land, Context.Guild))
                {
                    await SafeReplyAsync("", embed: emb);
                    await Task.Delay(TimeSpan.FromSeconds(2));
                }
            }
        }

        [Command("kraina dodaj", RunMode = RunMode.Async)]
        [Alias("land add")]
        [Summary("dodaje użytkownika do krainy")]
        [Remarks("Karna Kotleciki")]
        public async Task AddPersonAsync([Summary("nazwa użytkownika")]SocketGuildUser user, [Summary("nazwa krainy")][Remainder]string name = null)
        {
            using (var db = new Database.DatabaseContext(Config))
            {
                var config = await db.GetCachedGuildFullConfigAsync(Context.Guild.Id);
                var land = _manager.DetermineLand(config.Lands, Context.User as SocketGuildUser, name);
                if (land == null)
                {
                    await SafeReplyAsync("", embed: "Nie zarządzasz żadną krainą.".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                var role = Context.Guild.GetRole(land.Underling);
                if (role == null)
                {
                    await SafeReplyAsync("", embed: "Nie odnaleziono roli członka!".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                if (!user.Roles.Contains(role))
                    await user.AddRoleAsync(role);

                await SafeReplyAsync("", embed: $"{user.Mention} dołącza do `{land.Name}`.".ToEmbedMessage(EMType.Success).Build());
            }
        }

        [Command("kraina usuń", RunMode = RunMode.Async)]
        [Alias("land remove", "kraina usun")]
        [Summary("usuwa użytkownika z krainy")]
        [Remarks("Karna")]
        public async Task RemovePersonAsync([Summary("nazwa użytkownika")]SocketGuildUser user, [Summary("nazwa krainy")][Remainder]string name = null)
        {
            using (var db = new Database.DatabaseContext(Config))
            {
                var config = await db.GetCachedGuildFullConfigAsync(Context.Guild.Id);
                var land = _manager.DetermineLand(config.Lands, Context.User as SocketGuildUser, name);
                if (land == null)
                {
                    await SafeReplyAsync("", embed: "Nie zarządzasz żadną krainą.".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                var role = Context.Guild.GetRole(land.Underling);
                if (role == null)
                {
                    await SafeReplyAsync("", embed: "Nie odnaleziono roli członka!".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                if (user.Roles.Contains(role))
                    await user.RemoveRoleAsync(role);

                await SafeReplyAsync("", embed: $"{user.Mention} odchodzi z `{land.Name}`.".ToEmbedMessage(EMType.Success).Build());
            }
        }
    }
}