#pragma warning disable 1591

using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Sanakan.Config;
using Sanakan.Extensions;
using Sanakan.Services.PocketWaifu;
using Sanakan.TypeReaders;
using Z.EntityFramework.Plus;

namespace Sanakan.Services.Session.Models
{
    public class AcceptProfileCode : IAcceptActions
    {
        public IMessage Message { get; set; }
        public IUser User { get; set; }
        public ProfileCode Code { get; set; }
        public CurrencyType Currency { get; set; }
        public long Total { get; set; }
        public string PendingImage { get; set; }

        private IConfig _config;

        public AcceptProfileCode(IConfig config)
        {
            _config = config;
        }

        public async Task<bool> OnAccept(SessionContext context)
        {
            using (var db = new Database.DatabaseContext(_config))
            {
                var botuser = await db.GetUserOrCreateSimpleAsync(User.Id);
                var newImage = PendingImage != null;

                // the profile could have changed since the preview, so the shown price must still be valid
                var invalid = Code.Validate(botuser, newImage, ProfileConfigTypeReader.IsSelectableBorder) != null
                    || Code.GetCosts(botuser, newImage, Currency).Sum(x => x.Cost.Cost) != Total
                    || (newImage && !File.Exists(PendingImage));

                if (invalid)
                {
                    await Message.Channel.SendMessageAsync("", embed: $"{User.Mention} profil zmienił się od podglądu, użyj polecenia ponownie.".ToEmbedMessage(EMType.Error).Build());
                    return true;
                }

                if (Total > 0 && !botuser.Pay(new CurrencyCost((int)Total, Currency)))
                {
                    await Message.Channel.SendMessageAsync("", embed: $"{User.Mention} nie masz wystarczającej liczby **{Currency}**, potrzebujesz {Total}.".ToEmbedMessage(EMType.Error).Build());
                    return true;
                }

                if (newImage)
                {
                    var styleUri = $"{Dir.SavedData}/SR{botuser.Id}.webp";
                    File.Move(PendingImage, styleUri, true);
                    botuser.StatsReplacementProfileUri = styleUri;
                }

                Code.ApplyTo(botuser);
                await db.SaveChangesAsync();

                QueryCacheManager.ExpireTag(new string[] { CacheTags.User(botuser.Id) });

                await Message.Channel.SendMessageAsync("", embed: $"{User.Mention} ustawiono profil z kodu `{Code.Encode()}`.".ToEmbedMessage(EMType.Success).Build());
            }
            return true;
        }

        public Task<bool> OnDecline(SessionContext context)
        {
            if (PendingImage != null && File.Exists(PendingImage))
                File.Delete(PendingImage);

            return Task.FromResult(true);
        }
    }
}
