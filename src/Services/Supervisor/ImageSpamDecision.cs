#pragma warning disable 1591

namespace Sanakan.Services.Supervisor
{
    public sealed class ImageSpamDecision
    {
        public const int SpamWindowMinutes = 2;
        public const int ScamWindowMinutes = 5;

        internal ImageSpamDecision(int oversizedMessages, int scamImages)
        {
            OversizedMessages = oversizedMessages;
            ScamImages = scamImages;
        }

        public int OversizedMessages { get; }
        public int ScamImages { get; }
        public bool ShouldDelete => OversizedMessages > 0 || ScamImages > 0;
        public bool ShouldPunish => ScamImages >= 2 || OversizedMessages >= 3;
        public bool ShouldWarn => !ShouldPunish && (ScamImages == 1 || OversizedMessages == 1);

        public string GetPenaltyReason(bool ban)
        {
            var penalty = ban ? "ban" : "mute";
            if (ScamImages >= 2)
                return $"Automatyczny {penalty}: scamowe obrazki - rozpoznano {ScamImages} scamowych obrazków w ciągu {ScamWindowMinutes} minut.";

            if (OversizedMessages >= 3)
                return $"Automatyczny {penalty}: spam obrazkami - wysłano {OversizedMessages} wiadomości zawierających więcej niż trzy obrazki w ciągu {SpamWindowMinutes} minut.";

            return null;
        }
    }
}
