#pragma warning disable 1591

using Discord.Commands;
using Sanakan.Services.PocketWaifu;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using Sanakan.Database.Models;
using Sanakan.Extensions;

namespace Sanakan.TypeReaders
{
    public class ProfileConfigTypeReader : TypeReader
    {
        private class NamePair<T>
        {
            public NamePair(string name, T type, bool strict = false)
            {
                Name = name;
                Type = type;
                StrictMatching = strict;
            }

            public bool StrictMatching;
            public string Name;
            public T Type;
        }

        private static readonly List<NamePair<ProfileConfigType>> _profileConfigTypes = new List<NamePair<ProfileConfigType>>
        {
            new NamePair<ProfileConfigType>("info", ProfileConfigType.ShowInfo),
            new NamePair<ProfileConfigType>("jestem leniwy", ProfileConfigType.BackgroundAndStyle),
            new NamePair<ProfileConfigType>("tło", ProfileConfigType.Background),
            new NamePair<ProfileConfigType>("background", ProfileConfigType.Background),
            new NamePair<ProfileConfigType>("style", ProfileConfigType.Style),
            new NamePair<ProfileConfigType>("styl", ProfileConfigType.Style),
            new NamePair<ProfileConfigType>("nakładka", ProfileConfigType.Overlay),
            new NamePair<ProfileConfigType>("overlay", ProfileConfigType.Overlay),
            new NamePair<ProfileConfigType>("ramka awatara", ProfileConfigType.AvatarBorder),
            new NamePair<ProfileConfigType>("avatar border", ProfileConfigType.AvatarBorder),
            new NamePair<ProfileConfigType>("przeźroczystość cieni", ProfileConfigType.ShadowsOpacity),
            new NamePair<ProfileConfigType>("shadows opacity", ProfileConfigType.ShadowsOpacity),

            new NamePair<ProfileConfigType>("pasek", ProfileConfigType.Bar),
            new NamePair<ProfileConfigType>("bar", ProfileConfigType.Bar),
            new NamePair<ProfileConfigType>("mini waifu", ProfileConfigType.MiniFavCard),
            new NamePair<ProfileConfigType>("anime", ProfileConfigType.AnimeStats),
            new NamePair<ProfileConfigType>("manga", ProfileConfigType.MangaStats),
            new NamePair<ProfileConfigType>("karcianka", ProfileConfigType.CardsStats),
            new NamePair<ProfileConfigType>("game stats", ProfileConfigType.CardsStats),
            new NamePair<ProfileConfigType>("mini galeria", ProfileConfigType.MiniGallery),
            new NamePair<ProfileConfigType>("mini gallery", ProfileConfigType.MiniGallery),
            new NamePair<ProfileConfigType>("ilość kart mini galerii", ProfileConfigType.CardCntInMiniGallery),
            new NamePair<ProfileConfigType>("card count in mini gallery", ProfileConfigType.CardCntInMiniGallery),
            new NamePair<ProfileConfigType>("zamiana paneli", ProfileConfigType.FlipPanels),
            new NamePair<ProfileConfigType>("flip panels", ProfileConfigType.FlipPanels),
            new NamePair<ProfileConfigType>("ramka na poziom", ProfileConfigType.LevelAvatarBorder),
            new NamePair<ProfileConfigType>("border per level", ProfileConfigType.LevelAvatarBorder),
        };

        private static readonly List<NamePair<ProfileType>> _profileStyleTypes = new List<NamePair<ProfileType>>
        {
            new NamePair<ProfileType>("stats on image", ProfileType.StatsOnImg),
            new NamePair<ProfileType>("stats", ProfileType.Stats, true),
            new NamePair<ProfileType>("statystyki na obrazku", ProfileType.StatsOnImg),
            new NamePair<ProfileType>("statystyki", ProfileType.Stats, true),
            new NamePair<ProfileType>("obrazek na statystykach", ProfileType.StatsWithImg),
            new NamePair<ProfileType>("obrazek", ProfileType.Img, true),
            new NamePair<ProfileType>("image on stats", ProfileType.StatsWithImg),
            new NamePair<ProfileType>("image", ProfileType.Img, true),
            new NamePair<ProfileType>("duża galeria na obrazku", ProfileType.CardsOnImg),
            new NamePair<ProfileType>("duża galeria", ProfileType.Cards, true),
            new NamePair<ProfileType>("big gallery on image", ProfileType.CardsOnImg),
            new NamePair<ProfileType>("big gallery", ProfileType.Cards, true),
            new NamePair<ProfileType>("galeria z karcianką na obrazku", ProfileType.MiniGalleryOnImg),
            new NamePair<ProfileType>("galeria z karcianką", ProfileType.MiniGallery, true),
            new NamePair<ProfileType>("gallery with game stats on image", ProfileType.MiniGalleryOnImg),
            new NamePair<ProfileType>("gallery with game stats", ProfileType.MiniGallery, true),
        };

        private static readonly List<NamePair<AvatarBorder>> _avatarBorderTypes = new List<NamePair<AvatarBorder>>
        {
            new NamePair<AvatarBorder>("brak", AvatarBorder.None),
            new NamePair<AvatarBorder>("none", AvatarBorder.None),
            new NamePair<AvatarBorder>("liście", AvatarBorder.PurpleLeaves),
            new NamePair<AvatarBorder>("leaves", AvatarBorder.PurpleLeaves),
            new NamePair<AvatarBorder>("dzidowy", AvatarBorder.Dzedai),
            new NamePair<AvatarBorder>("domyślny", AvatarBorder.Base),
            new NamePair<AvatarBorder>("default", AvatarBorder.Base),
            new NamePair<AvatarBorder>("woda", AvatarBorder.Water),
            new NamePair<AvatarBorder>("water", AvatarBorder.Water),
            new NamePair<AvatarBorder>("kruki", AvatarBorder.Crows),
            new NamePair<AvatarBorder>("crows", AvatarBorder.Crows),
            new NamePair<AvatarBorder>("wstążka", AvatarBorder.Bow),
            new NamePair<AvatarBorder>("bow", AvatarBorder.Bow),
            new NamePair<AvatarBorder>("metalowa", AvatarBorder.Metal),
            new NamePair<AvatarBorder>("metal", AvatarBorder.Metal),
            new NamePair<AvatarBorder>("kwiatki", AvatarBorder.RedThinLeaves),
            new NamePair<AvatarBorder>("flowers", AvatarBorder.RedThinLeaves),
            new NamePair<AvatarBorder>("czaszka", AvatarBorder.Skull),
            new NamePair<AvatarBorder>("skull", AvatarBorder.Skull),
            new NamePair<AvatarBorder>("ogień", AvatarBorder.Fire),
            new NamePair<AvatarBorder>("fire", AvatarBorder.Fire),
            new NamePair<AvatarBorder>("lód", AvatarBorder.Ice),
            new NamePair<AvatarBorder>("ice", AvatarBorder.Ice),
            new NamePair<AvatarBorder>("promium", AvatarBorder.Promium),
            new NamePair<AvatarBorder>("złota", AvatarBorder.Gold),
            new NamePair<AvatarBorder>("gold", AvatarBorder.Gold),
            new NamePair<AvatarBorder>("czerwona", AvatarBorder.Red),
            new NamePair<AvatarBorder>("red", AvatarBorder.Red),
            new NamePair<AvatarBorder>("tęcza", AvatarBorder.Rainbow),
            new NamePair<AvatarBorder>("rainbow", AvatarBorder.Rainbow),
            new NamePair<AvatarBorder>("różowa", AvatarBorder.Pink),
            new NamePair<AvatarBorder>("pink", AvatarBorder.Pink),
            new NamePair<AvatarBorder>("prosta", AvatarBorder.Simple),
            new NamePair<AvatarBorder>("simple", AvatarBorder.Simple),
        };

        private static CurrencyType ParseCurrency(ReadOnlySpan<char> chars)
        {
            if (chars.Length != 2)
                return CurrencyType.SC;

            return chars.Equals("TC", StringComparison.OrdinalIgnoreCase) ? CurrencyType.TC : CurrencyType.SC;
        }

        private static bool NeedMoreParams(ProfileConfigType type) => type switch
        {
            ProfileConfigType.ShowInfo              => false,
            ProfileConfigType.Bar                   => false,
            ProfileConfigType.MiniFavCard           => false,
            ProfileConfigType.AnimeStats            => false,
            ProfileConfigType.MangaStats            => false,
            ProfileConfigType.CardsStats            => false,
            ProfileConfigType.MiniGallery           => false,
            ProfileConfigType.CardCntInMiniGallery  => false,
            ProfileConfigType.FlipPanels            => false,
            ProfileConfigType.LevelAvatarBorder     => false,
            _ => true
        };

        private static ProfileSettings ProfileConfigTypeToProfileSettings(ProfileConfigType type) => type switch
        {
            ProfileConfigType.Bar                   => ProfileSettings.BarOnTop,
            ProfileConfigType.MiniFavCard           => ProfileSettings.ShowWaifu,
            ProfileConfigType.AnimeStats            => ProfileSettings.ShowAnime,
            ProfileConfigType.MangaStats            => ProfileSettings.ShowManga,
            ProfileConfigType.CardsStats            => ProfileSettings.ShowCards,
            ProfileConfigType.MiniGallery           => ProfileSettings.ShowGallery,
            ProfileConfigType.CardCntInMiniGallery  => ProfileSettings.HalfGallery,
            ProfileConfigType.FlipPanels            => ProfileSettings.Flip,
            ProfileConfigType.LevelAvatarBorder     => ProfileSettings.BorderColor,
            _ => ProfileSettings.None
        };

        public override Task<TypeReaderResult> ReadAsync(ICommandContext context, string input, IServiceProvider services)
        {
            var config = new ProfileConfig { Currency = CurrencyType.SC, Type = ProfileConfigType.ShowInfo, ToggleCurentValue = false };
            if (!string.IsNullOrEmpty(input))
            {
                var selectedType = _profileConfigTypes.FirstOrDefault(x => input.StartsWith(x.Name, StringComparison.OrdinalIgnoreCase));
                if (selectedType is null)
                {
                    return Task.FromResult(TypeReaderResult.FromError(CommandError.ParseFailed, "Nie rozpoznano typu konfiguracji!"));
                }

                config.Type = selectedType.Type;
                var strippedInput = input.AsSpan().Slice(selectedType.Name.Length);
                if (NeedMoreParams(config.Type))
                {
                    if (strippedInput.IsEmpty)
                    {
                        return Task.FromResult(TypeReaderResult.FromError(CommandError.ParseFailed, "Nie podano parametrów konfiguracji!"));
                    }
                    strippedInput = strippedInput.TrimStart();
                }

                switch (config.Type)
                {
                    case ProfileConfigType.ShowInfo:
                    {
                        if (!strippedInput.IsEmpty && int.TryParse(strippedInput.Trim(), out var option))
                        {
                            config.Value = option < 1 ? 1 : option;
                        }
                        return Task.FromResult(TypeReaderResult.FromSuccess(config));
                    }

                    case ProfileConfigType.Overlay:
                    case ProfileConfigType.Background:
                    {
                        var spaceIdx = strippedInput.IndexOf(' ');
                        var url = spaceIdx == -1 ? strippedInput : strippedInput.Slice(0, spaceIdx);
                        strippedInput = spaceIdx == -1 ? Span<char>.Empty : strippedInput.Slice(spaceIdx);

                        config.Url = url.Trim().ToString();
                    }
                    break;

                    case ProfileConfigType.BackgroundAndStyle:
                    case ProfileConfigType.Style:
                    {
                        input = strippedInput.ToString();
                        var selectedStyleType = _profileStyleTypes.FirstOrDefault(x => input.StartsWith(x.Name, StringComparison.OrdinalIgnoreCase));
                        if (selectedStyleType is null)
                        {
                            return Task.FromResult(TypeReaderResult.FromError(CommandError.ParseFailed, "Nie rozpoznano stylu profilu!"));
                        }

                        config.Style = selectedStyleType.Type;
                        strippedInput = strippedInput.Slice(selectedStyleType.Name.Length);

                        if (selectedStyleType.StrictMatching)
                        {
                            if (!strippedInput.IsEmpty)
                            {
                                var matchingOption = _profileStyleTypes[_profileStyleTypes.IndexOf(selectedStyleType) - 1];
                                var wordsToCheck = matchingOption.Name.Substring(selectedStyleType.Name.Length).Trim().Split(" ");
                                var restOfInput = strippedInput.ToString();

                                if (wordsToCheck.Any(x => restOfInput.Contains(x, StringComparison.OrdinalIgnoreCase)))
                                {
                                    return Task.FromResult(TypeReaderResult.FromError(CommandError.ParseFailed, "Nie rozpoznano stylu profilu!"));
                                }
                            }
                        }

                        if (config.StyleNeedUrl())
                        {
                            if (strippedInput.IsEmpty)
                            {
                                return Task.FromResult(TypeReaderResult.FromError(CommandError.ParseFailed, "Nie podano parametrów konfiguracji stylu!"));
                            }
                            strippedInput = strippedInput.TrimStart();
                            goto case ProfileConfigType.Background;
                        }
                    }
                    break;

                    case ProfileConfigType.Bar:
                    case ProfileConfigType.MiniFavCard:
                    case ProfileConfigType.AnimeStats:
                    case ProfileConfigType.MangaStats:
                    case ProfileConfigType.CardsStats:
                    case ProfileConfigType.MiniGallery:
                    case ProfileConfigType.CardCntInMiniGallery:
                    case ProfileConfigType.FlipPanels:
                    case ProfileConfigType.LevelAvatarBorder:
                    {
                        config.Settings = ProfileConfigTypeToProfileSettings(config.Type);
                        config.ToggleCurentValue = config.Settings != ProfileSettings.None;
                    }
                    break;

                    case ProfileConfigType.ShadowsOpacity:
                    {
                        var spaceIdx = strippedInput.IndexOf(' ');
                        var value = spaceIdx == -1 ? strippedInput : strippedInput.Slice(0, spaceIdx);
                        strippedInput = spaceIdx == -1 ? Span<char>.Empty : strippedInput.Slice(spaceIdx);
                        bool success = int.TryParse(value.Trim(), out var percent);
                        if (!success || percent < 0 || percent > 100)
                        {
                            return Task.FromResult(TypeReaderResult.FromError(CommandError.ParseFailed, "Wartość przeźroczystości jest niepoprawna!"));
                        }

                        config.Value = percent;
                    }
                    break;

                    case ProfileConfigType.AvatarBorder:
                    {
                        input = strippedInput.ToString();
                        var selectedAvatarType = _avatarBorderTypes.FirstOrDefault(x => input.StartsWith(x.Name, StringComparison.OrdinalIgnoreCase));
                        if (selectedAvatarType is null)
                        {
                            return Task.FromResult(TypeReaderResult.FromError(CommandError.ParseFailed, "Nie rozpoznano ramki awatara!"));
                        }

                        config.Border = selectedAvatarType.Type;
                        strippedInput = strippedInput.Slice(selectedAvatarType.Name.Length);
                    }
                    break;

                    default:
                        return Task.FromResult(TypeReaderResult.FromError(CommandError.ParseFailed, "Nie rozpoznano typu konfiguracji!"));
                }

                if (!strippedInput.IsEmpty)
                {
                    config.Currency = ParseCurrency(strippedInput.TrimStart());
                }

            }
            return Task.FromResult(TypeReaderResult.FromSuccess(config));
        }
    }
}