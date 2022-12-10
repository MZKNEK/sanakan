#pragma warning disable 1591

using Discord;

namespace Sanakan.Extensions
{
    public static class EmbedFooterBuilderExtension
    {
        public static EmbedBuilder WithOwner(this EmbedBuilder builder, IUser user)
        {
            return builder.WithFooter(new EmbedFooterBuilder().WithOwner(user));
        }

        public static EmbedFooterBuilder WithOwner(this EmbedFooterBuilder builder, IUser user)
        {
            if (user == null) return builder.WithText("Należy do: ????");
            return builder.WithText("Należy do: " + user.GetUserNickInGuild());
        }
    }
}
