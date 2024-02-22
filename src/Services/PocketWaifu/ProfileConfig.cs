using System.Collections.Generic;
using System.Linq;
using System.Text;
using Sanakan.Database.Models;

namespace Sanakan.Services.PocketWaifu
{
    public enum ProfileConfigType
    {
        ShowInfo,
        BackgroundAndStyle,
        Background,
        Style,
        Overlay,
        AvatarBorder,
        ShadowsOpacity,
        PremiumOverlay,

        Bar,
        MiniFavCard,
        AnimeStats,
        MangaStats,
        CardsStats,
        MiniGallery,
        CardCntInMiniGallery,
        FlipPanels,
        LevelAvatarBorder,
        RoundAvatarWithoutBorder,
        CustomBarOpacity
    }

    public class ProfileConfig
    {
        private class OptionInfo
        {
            public OptionInfo(string name, string desc, string example = null,
                string param = null, string price = null, string options = null)
            {
                Name = name;
                Desc = desc;
                Params = param;
                Example = example;
                Options = options;
                Price = price;
            }

            public string Name;
            public string Params;
            public string Example;
            public string Desc;
            public string Price;
            public string Options;
        }

        private readonly static List<OptionInfo> _help =  new List<OptionInfo>()
        {
            new OptionInfo("jestem leniwy", "Ustawia tło profilu od razu z wybranym stylem, wymaga podania obrazka - 750 x 500px.",
                "konfiguracja profilu jestem leniwy https://sanakan.pl/i/example_profile_full.png", "`typ` `bezpośredni link do obrazka`",
                $"{ToPay(ProfileConfigType.BackgroundAndStyle, CurrencyType.SC)} / {ToPay(ProfileConfigType.BackgroundAndStyle, CurrencyType.TC)}",
                $"**[2]** obrazek\n**[3]** obrazek na statystykach\n**[5]** duża galeria na obrazku\n**[6]** statystyki na obrazku\n**[8]** galeria z karcianką na obrazku"),

            new OptionInfo("tło", "Pozwala ustwić obrazek w górnej części profilu o wymiarach 750 x 160px.",
                "konfiguracja profilu tło https://sanakan.pl/i/example_new_profile_bg.png", "`bezpośredni link do obrazka`",
                $"{ToPay(ProfileConfigType.Background, CurrencyType.SC)} / {ToPay(ProfileConfigType.Background, CurrencyType.TC)}"),

            new OptionInfo("styl", "Pozwala zmienić dolny wygląd profilu oraz ustawić tam obrazek o wymiarach 750 x 340px, gdy styl tego wymaga.",
                "konfiguracja profilu styl obrazek https://sanakan.pl/i/example_new_style_1.png", "`typ` `bezpośredni link do obrazka (opcjonalne)`",
                $"{ToPay(ProfileConfigType.Style, CurrencyType.SC)} / {ToPay(ProfileConfigType.Style, CurrencyType.TC)}",
                $"**[1]** statystyki\n**[2]** obrazek *(wymagany link)*\n**[3]** obrazek na statystykach *(wymagany link)*\n**[4]** duża galeria\n**[5]** duża galeria na obrazku *(wymagany link)*\n**[6]** statystyki na obrazku *(wymagany link)*\n**[7]** galeria z karcianką\n**[8]** galeria z karcianką na obrazku *(wymagany link)*"),

            new OptionInfo("nakładka", "Pozwala ustwić obrazek będący prawie nad wszystkimi elementami w profilu zaczynający się od czarnego paska z nazwą użytkownika i idący do dołu profilu o wymiarach 750 x 402px.\n\n*Jeśli nakładka zasłoni nazwę użytkownika, to należy ją umieścić na nakładce w innym miejscu. W przeciwnym razie nakładka zostanie usunięta.*",
                "konfiguracja profilu nakładka https://sanakan.pl/i/example_profile_overlay.png", "`bezpośredni link do obrazka`",
                $"{ToPay(ProfileConfigType.Overlay, CurrencyType.SC)} / {ToPay(ProfileConfigType.Overlay, CurrencyType.TC)}"),

            new OptionInfo("ramka awatara", "Pozwala zmienić wygląd ramki awatara.",
                "konfiguracja profilu ramka awatara domyślny", "`typ`",
                $"{ToPay(ProfileConfigType.AvatarBorder, CurrencyType.SC)} / {ToPay(ProfileConfigType.AvatarBorder, CurrencyType.TC)}",
                $"**[1]** brak *(darmowa)*\n**[2]** domyślny *(darmowa)*\n**[3]** liście\n**[4]** dzidowy\n**[5]** woda\n**[6]** kruki\n**[7]** wstążka\n**[8]** metalowa\n**[9]** kwiatki\n**[10]** czaszka\n**[11]** ogień\n**[12]** lód\n**[13]** promium\n**[14]** złota\n**[15]** czerwona\n**[16]** tęcza\n**[17]** różowa\n**[18]** prosta"),

            new OptionInfo("przeźroczystość cieni", "Pozwala zmienić przeźroczystość czarnych cieni pod panelami profilu na wybranych stylach.",
                "konfiguracja profilu przeźroczystość cieni 30", "`procent`"),

            new OptionInfo("ultra nakładka", "Pozwala ustwić obrazek będący nad wszystkimi elementami w profilu poza paskiem o wymiarach 750 x 500px.\n\n*Jeśli nakładka zasłoni nazwę użytkownika, to należy ją umieścić na nakładce w innym miejscu. W przeciwnym razie nakładka zostanie usunięta.*",
                "konfiguracja profilu ultra nakładka https://sanakan.pl/i/example_profile_ult_overlay.png", "`bezpośredni link do obrazka`",
                $"{ToPay(ProfileConfigType.PremiumOverlay, CurrencyType.SC)} / {ToPay(ProfileConfigType.PremiumOverlay, CurrencyType.TC)}"),

            new OptionInfo("pasek", "Pozwala zmienić pozycję paska profilu na górę lub dół."),
            new OptionInfo("mini waifu", "Pozwala zmienić widoczność karty ustawionej jako waifu w prawym górnym rogu profilu."),
            new OptionInfo("anime", "Pozwala zmienić widoczność panelu statystyk anime, wymaga stylu wyświetlającego statystyki."),
            new OptionInfo("manga", "Pozwala zmienić widoczność panelu statystyk mang, wymaga stylu wyświetlającego statystyki."),
            new OptionInfo("karcianka", "Pozwala zmienić widoczność panelu statystyk karcianki, wymaga stylu wyświetlającego statystyki."),
            new OptionInfo("mini galeria", "Pozwala zmienić widoczność panelu mini galerii, wymaga stylu wyświetlającego statystyki oraz mini galerię."),
            new OptionInfo("ilość kart mini galerii", "Pozwala zmienić liczbe kart w mini galerii między 2 a 6, wymaga stylu wyświetlającego statystyki oraz mini galerie."),
            new OptionInfo("zamiana paneli", "Pozwala zamienić między sobą prawy i lewy panel wymaga stylu wyświetlającego statystyki."),
            new OptionInfo("ramka na poziom", "Pozwala aktywować ramkę awatara zależną od poziomu która działa wyłącznie z niektórymi ramkami."),
            new OptionInfo("okrągły awatar", "Pozwala aktywować zaokrąglony awatar, gdy nie mamy ustawionej ramki."),
            new OptionInfo("przeźroczysty pasek", "Pozwala aktywować ustawiony stopień przeźroczystości cieni dla paska."),
        };

        public ProfileConfigType Type;
        public bool ToggleCurentValue;
        public string Url;
        public int Value;

        public ProfileType Style;
        public AvatarBorder Border;
        public ProfileSettings Settings;
        public CurrencyType Currency;

        public bool NeedPay() => Type switch
        {
            ProfileConfigType.Style => true,
            ProfileConfigType.Overlay => true,
            ProfileConfigType.Background => true,
            ProfileConfigType.PremiumOverlay => true,
            ProfileConfigType.BackgroundAndStyle => true,
            ProfileConfigType.AvatarBorder => Border != AvatarBorder.None && Border != AvatarBorder.Base,
            _ => false
        };

        private static CurrencyCost ToPay(ProfileConfigType type, CurrencyType currency) => type switch
        {
            ProfileConfigType.Style => new CurrencyCost(currency == CurrencyType.SC ? 3000 : 1000, currency),
            ProfileConfigType.Overlay => new CurrencyCost(currency == CurrencyType.SC ? 4000 : 1800, currency),
            ProfileConfigType.Background => new CurrencyCost(currency == CurrencyType.SC ? 5000 : 2500, currency),
            ProfileConfigType.PremiumOverlay => new CurrencyCost(currency == CurrencyType.SC ? 120000 : 25000, currency),
            ProfileConfigType.BackgroundAndStyle => new CurrencyCost(currency == CurrencyType.SC ? 10000 : 4000, currency),
            ProfileConfigType.AvatarBorder => new CurrencyCost(currency == CurrencyType.SC ? 25000 : 8000, currency),
            _ => new CurrencyCost(0, currency)
        };

        public CurrencyCost ToPay() => ToPay(Type, Currency);

        public bool StyleNeedUrl() => Style switch
        {
            ProfileType.Stats       => false,
            ProfileType.Cards       => false,
            ProfileType.MiniGallery => false,
            _ => true
        };

        public float PercentToOpacity() => 1.0f - (Value / 100.0f);

        public bool CanUseSettingOnStyle(ProfileType style) => Settings switch
        {
            ProfileSettings.None => true,
            ProfileSettings.ShowWaifu => true,
            ProfileSettings.BarOnTop => true,
            ProfileSettings.BorderColor => true,
            ProfileSettings.RoundAvatar => true,
            ProfileSettings.BarOpacity => true,
            ProfileSettings.HalfGallery => style == ProfileType.MiniGallery || style == ProfileType.MiniGalleryOnImg,
            ProfileSettings.ShowGallery => style == ProfileType.MiniGallery || style == ProfileType.MiniGalleryOnImg,
            ProfileSettings.ShowAnime => IsConfigurableStyle(style) && style != ProfileType.MiniGallery && style != ProfileType.MiniGalleryOnImg,
            ProfileSettings.ShowManga => IsConfigurableStyle(style) && style != ProfileType.MiniGallery && style != ProfileType.MiniGalleryOnImg,
            _ => IsConfigurableStyle(style)
        };

        public bool IsConfigurableStyle(ProfileType style) => style switch
        {
            ProfileType.Img => false,
            ProfileType.Cards => false,
            _ => true
        };

        public string GetHelp()
        {
            if (Value < 1 || Value > _help.Count)
            {
                return $"**Konfiguracja profilu:**\n"
                     + $"*Użyj* `konfiguracja profilu info [nr opcji]` *aby wyświetlić szczegóły.*\n\n"
                     + $"**Opcje:**\n{string.Join("\n", _help.Select((x, i) => $"**[{i+1}]** {x.Name}"))}\n\n"
                     + $"Dodaj na koniec polecenia **TC** by zmienić rodzaj waluty przy płatnych opcjach *(domyślna waluta to SC)*.";
            }
            var cmd =  _help[Value-1];
            var help = new StringBuilder().Append($"**Konfiguracja profilu:**\n\n**{cmd.Name}** {cmd.Params}\n{cmd.Desc}");
            if (!string.IsNullOrEmpty(cmd.Options)) help.Append($"\n\n**Typy:**\n{cmd.Options}");
            if (!string.IsNullOrEmpty(cmd.Price)) help.Append($"\n\n**Cena użycia:** {cmd.Price}");
            if (!string.IsNullOrEmpty(cmd.Example)) help.Append($"\n\n*np.* `{cmd.Example}`");
            return help.ToString();
        }

        public string What() => Type switch
        {
            ProfileConfigType.Style => "zmieniony został styl profilu.",
            ProfileConfigType.BackgroundAndStyle => "zmieniony został styl oraz tło profilu.",
            ProfileConfigType.Background => "zmienione zostało tło profilu",
            ProfileConfigType.AvatarBorder => "zmieniona została ramka awatara.",
            ProfileConfigType.AnimeStats => "zmieniona została widoczność panelu z statystykami anime.",
            ProfileConfigType.MangaStats => "zmieniona została widoczność panelu z statystykami mang.",
            ProfileConfigType.Bar => "zmieniona została pozycja paska profilu.",
            ProfileConfigType.CardCntInMiniGallery => "zmieniona została liczba kart w mini galerii.",
            ProfileConfigType.CardsStats => "zmieniona została widoczność panelu z statystykami karcianki.",
            ProfileConfigType.FlipPanels => "panele stylu zostały ze sobą zamienione.",
            ProfileConfigType.MiniFavCard => "zmieniona została widoczność karty ustawionej jako waifu w prawym górnym rogu profilu.",
            ProfileConfigType.MiniGallery => "zmieniona została widoczność panelu mini galerii.",
            ProfileConfigType.Overlay => "zmieniona została nakładka na profil.",
            ProfileConfigType.PremiumOverlay => "zmieniona została ultra nakładka na profil.",
            ProfileConfigType.ShadowsOpacity => "zmieniona została przeźroczystość cieni paneli stylu.",
            ProfileConfigType.LevelAvatarBorder => "przełączono ustawienia ramek na poziom.",
            ProfileConfigType.RoundAvatarWithoutBorder => "przełączono rodzaj awatara bez ramki.",
            ProfileConfigType.CustomBarOpacity => "przełączono przeźroczystość pasków profilu.",
            _ => "????"
        };
    }
}
