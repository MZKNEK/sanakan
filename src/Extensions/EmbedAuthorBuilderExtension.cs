#pragma warning disable 1591

using System;
using Discord;
using Discord.WebSocket;

namespace Sanakan.Extensions
{
    public static class EmbedAuthorBuilderExtension
    {
        public static string GetUserOrDefaultAvatarUrl(this IUser user, bool getFromGuild = false)
        {
            var avatar = user.GetAvatarUrl() ?? user.GetDefaultAvatarUrl();
            if (user is SocketGuildUser guildUser && getFromGuild)
            {
                var guildAvatar = guildUser.GetGuildAvatarUrl();
                if (guildAvatar != null) avatar = guildAvatar;
            }
            return avatar.Split("?")[0];
        }

        public static Func<string> PublicApiUrl { get; set; }

        // A CDN link dies once the user changes the avatar, an embed keeps it forever.
        public static string GetEmbedAvatarUrl(this IUser user, bool getFromGuild = false)
            => GetEmbedAvatarUrl(user, getFromGuild, PublicApiUrl?.Invoke());

        public static string GetEmbedAvatarUrl(IUser user, bool getFromGuild, string publicApiUrl)
        {
            if (string.IsNullOrWhiteSpace(publicApiUrl))
                return user.GetUserOrDefaultAvatarUrl(getFromGuild);

            var url = $"{publicApiUrl.TrimEnd('/')}/api/user/discord/{user.Id}/avatar";
            return getFromGuild && user is IGuildUser guildUser ? $"{url}?guild={guildUser.GuildId}" : url;
        }

        public static string GetUserNickInGuild(this IUser user)
        {
            if (user == null) return "????";
            if (user is SocketGuildUser guildUser)
            {
                return (guildUser.Nickname ?? user.GlobalName) ?? user.Username;
            }
            return user.GlobalName ?? user.Username;
        }

        public static EmbedBuilder WithUser(this EmbedBuilder builder, IUser user, bool includeId = false)
        {
            return builder.WithAuthor(new EmbedAuthorBuilder().WithUser(user, includeId));
        }

        public static EmbedAuthorBuilder WithUser(this EmbedAuthorBuilder builder, IUser user, bool includeId = false)
        {
            if (user == null) return builder.WithName("????");

            var id = includeId ? $" ({user.Id})" : "";

            if (user is SocketGuildUser sUser)
            {
                builder.WithName($"{(sUser.Nickname ?? sUser.GlobalName) ?? sUser.Username}{id}");
            }
            else
            {
                builder.WithName($"{user.GlobalName ?? user.Username}{id}");
            }

            return builder.WithIconUrl(user.GetEmbedAvatarUrl(true));
        }
    }
}
