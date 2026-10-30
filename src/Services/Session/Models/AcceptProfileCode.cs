#pragma warning disable 1591

using System.Collections.Generic;
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
        public bool StyleAttachment { get; set; }
        // the images already downloaded, by where they go; the background and style one comes split in two
        public Dictionary<ProfileImage, string> PendingImages { get; set; } = new Dictionary<ProfileImage, string>();

        private IConfig _config;

        public AcceptProfileCode(IConfig config)
        {
            _config = config;
        }

        public static string FinalPath(ProfileImage image, ulong userId) => image switch
        {
            ProfileImage.Background => $"{Dir.SavedData}/BG{userId}.webp",
            ProfileImage.Style => $"{Dir.SavedData}/SR{userId}.webp",
            ProfileImage.Overlay => $"{Dir.SavedData}/OV{userId}.webp",
            _ => $"{Dir.SavedData}/UOV{userId}.webp",
        };

        public static string PendingPath(ProfileImage image, ulong userId) => Path.ChangeExtension(FinalPath(image, userId), ".code.webp");

        public async Task<bool> OnAccept(SessionContext context)
        {
            using (var db = new Database.DatabaseContext(_config))
            {
                var botuser = await db.GetUserOrCreateSimpleAsync(User.Id);

                // the profile could have changed since the preview, so the shown price must still be valid
                var invalid = Code.Validate(botuser, StyleAttachment, ProfileConfigTypeReader.IsSelectableBorder) != null
                    || Code.GetCosts(botuser, StyleAttachment, Currency).Sum(x => x.Cost.Cost) != Total
                    || PendingImages.Values.Any(x => !File.Exists(x));

                if (invalid)
                {
                    DeletePending();
                    await Message.Channel.SendMessageAsync("", embed: $"{User.Mention} profil zmienił się od podglądu, użyj polecenia ponownie.".ToEmbedMessage(EMType.Error).Build());
                    return true;
                }

                if (Total > 0 && !botuser.Pay(new CurrencyCost((int)Total, Currency)))
                {
                    DeletePending();
                    await Message.Channel.SendMessageAsync("", embed: $"{User.Mention} nie masz wystarczającej liczby **{Currency}**, potrzebujesz {Total}.".ToEmbedMessage(EMType.Error).Build());
                    return true;
                }

                foreach (var (image, pending) in PendingImages)
                {
                    var final = FinalPath(image, botuser.Id);
                    File.Move(pending, final, true);
                    switch (image)
                    {
                        case ProfileImage.Background: botuser.BackgroundProfileUri = final; break;
                        case ProfileImage.Style: botuser.StatsReplacementProfileUri = final; break;
                        case ProfileImage.Overlay: botuser.CustomProfileOverlayUrl = final; break;
                        case ProfileImage.Premium: botuser.PremiumCustomProfileOverlayUrl = final; break;
                    }
                }

                Code.ApplyTo(botuser);
                await db.SaveChangesAsync();

                QueryCacheManager.ExpireTag(new string[] { CacheTags.User(botuser.Id) });

                await Message.Channel.SendMessageAsync("", embed: $"{User.Mention} ustawiono profil z kodu.".ToEmbedMessage(EMType.Success).Build());
            }
            return true;
        }

        public Task<bool> OnDecline(SessionContext context)
        {
            DeletePending();
            return Task.FromResult(true);
        }

        public void DeletePending()
        {
            foreach (var pending in PendingImages.Values)
                if (File.Exists(pending))
                    File.Delete(pending);
        }
    }
}
