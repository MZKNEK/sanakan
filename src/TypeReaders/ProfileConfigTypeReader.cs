#pragma warning disable 1591

using Discord.Commands;
using Sanakan.Services.PocketWaifu;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using Sanakan.Database.Models;
using Microsoft.IdentityModel.Tokens;
using Sanakan.Extensions;

namespace Sanakan.TypeReaders
{
    public class ProfileConfigTypeReader : TypeReader
    {
        private class NamePair<T>
        {
            public NamePair(string name, T type, uint index, bool strict = false)
            {
                Name = name;
                Type = type;
                Index = index;
                StrictMatching = strict;
            }

            public uint Index;
            public bool StrictMatching;
            public string Name;
            public T Type;
        }

        private static readonly List<NamePair<ProfileConfigType>> _profileConfigTypes = new List<NamePair<ProfileConfigType>>
        {
            new NamePair<ProfileConfigType>("info", ProfileConfigType.ShowInfo, 0),
            new NamePair<ProfileConfigType>("jestem leniwy", ProfileConfigType.BackgroundAndStyle, 1),
            new NamePair<ProfileConfigType>("tło", ProfileConfigType.Background, 2),
            new NamePair<ProfileConfigType>("background", ProfileConfigType.Background, 2),
            new NamePair<ProfileConfigType>("style", ProfileConfigType.Style, 3),
            new NamePair<ProfileConfigType>("styl", ProfileConfigType.Style, 3),
            new NamePair<ProfileConfigType>("nakładka", ProfileConfigType.Overlay, 4),
            new NamePair<ProfileConfigType>("overlay", ProfileConfigType.Overlay, 4),
            new NamePair<ProfileConfigType>("ramka awatara", ProfileConfigType.AvatarBorder, 5),
            new NamePair<ProfileConfigType>("avatar border", ProfileConfigType.AvatarBorder, 5),
            new NamePair<ProfileConfigType>("przeźroczystość cieni", ProfileConfigType.ShadowsOpacity, 6),
            new NamePair<ProfileConfigType>("shadows opacity", ProfileConfigType.ShadowsOpacity, 6),
            new NamePair<ProfileConfigType>("ultra nakładka", ProfileConfigType.PremiumOverlay, 7),
            new NamePair<ProfileConfigType>("ultra overlay", ProfileConfigType.PremiumOverlay, 7),
            new NamePair<ProfileConfigType>("pasek", ProfileConfigType.Bar, 8),
            new NamePair<ProfileConfigType>("bar", ProfileConfigType.Bar, 8),
            new NamePair<ProfileConfigType>("mini waifu", ProfileConfigType.MiniFavCard, 9),
            new NamePair<ProfileConfigType>("anime", ProfileConfigType.AnimeStats, 10),
            new NamePair<ProfileConfigType>("manga", ProfileConfigType.MangaStats, 11),
            new NamePair<ProfileConfigType>("karcianka", ProfileConfigType.CardsStats, 12),
            new NamePair<ProfileConfigType>("game stats", ProfileConfigType.CardsStats, 12),
            new NamePair<ProfileConfigType>("mini galeria", ProfileConfigType.MiniGallery, 13),
            new NamePair<ProfileConfigType>("mini gallery", ProfileConfigType.MiniGallery, 13),
            new NamePair<ProfileConfigType>("ilość kart mini galerii", ProfileConfigType.CardCntInMiniGallery, 14),
            new NamePair<ProfileConfigType>("card count in mini gallery", ProfileConfigType.CardCntInMiniGallery, 14),
            new NamePair<ProfileConfigType>("zamiana paneli", ProfileConfigType.FlipPanels, 15),
            new NamePair<ProfileConfigType>("flip panels", ProfileConfigType.FlipPanels, 15),
            new NamePair<ProfileConfigType>("ramka na poziom", ProfileConfigType.LevelAvatarBorder, 16),
            new NamePair<ProfileConfigType>("border per level", ProfileConfigType.LevelAvatarBorder, 16),
            new NamePair<ProfileConfigType>("okrągły awatar", ProfileConfigType.RoundAvatarWithoutBorder, 17),
            new NamePair<ProfileConfigType>("round avatar", ProfileConfigType.RoundAvatarWithoutBorder, 17),
            new NamePair<ProfileConfigType>("przeźroczysty pasek", ProfileConfigType.CustomBarOpacity, 18),
            new NamePair<ProfileConfigType>("transparent bar", ProfileConfigType.CustomBarOpacity, 18),
            new NamePair<ProfileConfigType>("widoczność nakładki", ProfileConfigType.OverlayVisibility, 19),
            new NamePair<ProfileConfigType>("overlay visibility", ProfileConfigType.OverlayVisibility, 19),
            new NamePair<ProfileConfigType>("widoczność ultra nakładki", ProfileConfigType.PremiumOverlayVisibility, 20),
            new NamePair<ProfileConfigType>("ultra overlay visibility", ProfileConfigType.PremiumOverlayVisibility, 20),
        };

        private static readonly List<NamePair<ProfileType>> _profileStyleTypes = new List<NamePair<ProfileType>>
        {
            new NamePair<ProfileType>("stats on image", ProfileType.StatsOnImg, 6),
            new NamePair<ProfileType>("stats", ProfileType.Stats, 1, true),
            new NamePair<ProfileType>("statystyki na obrazku", ProfileType.StatsOnImg, 6),
            new NamePair<ProfileType>("statystyki", ProfileType.Stats, 1, true),
            new NamePair<ProfileType>("obrazek na statystykach", ProfileType.StatsWithImg, 3),
            new NamePair<ProfileType>("obrazek", ProfileType.Img, 2, true),
            new NamePair<ProfileType>("image on stats", ProfileType.StatsWithImg, 3),
            new NamePair<ProfileType>("image", ProfileType.Img, 2, true),
            new NamePair<ProfileType>("duża galeria na obrazku", ProfileType.CardsOnImg, 5),
            new NamePair<ProfileType>("duża galeria", ProfileType.Cards, 4, true),
            new NamePair<ProfileType>("big gallery on image", ProfileType.CardsOnImg, 5),
            new NamePair<ProfileType>("big gallery", ProfileType.Cards, 4, true),
            new NamePair<ProfileType>("galeria z karcianką na obrazku", ProfileType.MiniGalleryOnImg, 8),
            new NamePair<ProfileType>("galeria z karcianką", ProfileType.MiniGallery, 7, true),
            new NamePair<ProfileType>("gallery with game stats on image", ProfileType.MiniGalleryOnImg, 8),
            new NamePair<ProfileType>("gallery with game stats", ProfileType.MiniGallery, 7, true),
        };

        private static readonly List<NamePair<AvatarBorder>> _avatarBorderTypes = new List<NamePair<AvatarBorder>>
        {
            new NamePair<AvatarBorder>("brak", AvatarBorder.None, 1),
            new NamePair<AvatarBorder>("none", AvatarBorder.None, 1),
            new NamePair<AvatarBorder>("domyślny", AvatarBorder.Base, 2),
            new NamePair<AvatarBorder>("default", AvatarBorder.Base, 2),
            new NamePair<AvatarBorder>("liście", AvatarBorder.PurpleLeaves, 3),
            new NamePair<AvatarBorder>("leaves", AvatarBorder.PurpleLeaves, 3),
            new NamePair<AvatarBorder>("dzidowy", AvatarBorder.Dzedai, 4),
            new NamePair<AvatarBorder>("woda", AvatarBorder.Water, 5),
            new NamePair<AvatarBorder>("water", AvatarBorder.Water, 5),
            new NamePair<AvatarBorder>("kruki", AvatarBorder.Crows, 6),
            new NamePair<AvatarBorder>("crows", AvatarBorder.Crows, 6),
            new NamePair<AvatarBorder>("wstążka", AvatarBorder.Bow, 7),
            new NamePair<AvatarBorder>("bow", AvatarBorder.Bow, 7),
            new NamePair<AvatarBorder>("metalowa", AvatarBorder.Metal, 8),
            new NamePair<AvatarBorder>("metal", AvatarBorder.Metal, 8),
            new NamePair<AvatarBorder>("kwiatki", AvatarBorder.RedThinLeaves, 9),
            new NamePair<AvatarBorder>("flowers", AvatarBorder.RedThinLeaves, 9),
            new NamePair<AvatarBorder>("czaszka", AvatarBorder.Skull, 10),
            new NamePair<AvatarBorder>("skull", AvatarBorder.Skull, 10),
            new NamePair<AvatarBorder>("ogień", AvatarBorder.Fire, 11),
            new NamePair<AvatarBorder>("fire", AvatarBorder.Fire, 11),
            new NamePair<AvatarBorder>("lód", AvatarBorder.Ice, 12),
            new NamePair<AvatarBorder>("ice", AvatarBorder.Ice, 12),
            new NamePair<AvatarBorder>("promium", AvatarBorder.Promium, 13),
            new NamePair<AvatarBorder>("złota", AvatarBorder.Gold, 14),
            new NamePair<AvatarBorder>("gold", AvatarBorder.Gold, 14),
            new NamePair<AvatarBorder>("czerwona", AvatarBorder.Red, 15),
            new NamePair<AvatarBorder>("red", AvatarBorder.Red, 15),
            new NamePair<AvatarBorder>("tęcza", AvatarBorder.Rainbow, 16),
            new NamePair<AvatarBorder>("rainbow", AvatarBorder.Rainbow, 16),
            new NamePair<AvatarBorder>("różowa", AvatarBorder.Pink, 17),
            new NamePair<AvatarBorder>("pink", AvatarBorder.Pink, 17),
            new NamePair<AvatarBorder>("prosta", AvatarBorder.Simple, 18),
            new NamePair<AvatarBorder>("simple", AvatarBorder.Simple, 18),
        };

        private static bool ParseCurrency(ReadOnlySpan<char> chars, out CurrencyType currency)
        {
            currency = CurrencyType.SC;
            if (chars.Length != 2)
                return false;

            if (chars.Equals("TC", StringComparison.OrdinalIgnoreCase))
            {
                currency = CurrencyType.TC;
                return true;
            }

            return chars.Equals("SC", StringComparison.OrdinalIgnoreCase);
        }

        private static bool NeedMoreParams(ProfileConfigType type) => type switch
        {
            ProfileConfigType.ShowInfo                 => false,
            ProfileConfigType.Bar                      => false,
            ProfileConfigType.MiniFavCard              => false,
            ProfileConfigType.AnimeStats               => false,
            ProfileConfigType.MangaStats               => false,
            ProfileConfigType.CardsStats               => false,
            ProfileConfigType.MiniGallery              => false,
            ProfileConfigType.CardCntInMiniGallery     => false,
            ProfileConfigType.FlipPanels               => false,
            ProfileConfigType.LevelAvatarBorder        => false,
            ProfileConfigType.RoundAvatarWithoutBorder => false,
            ProfileConfigType.CustomBarOpacity         => false,
            ProfileConfigType.OverlayVisibility        => false,
            ProfileConfigType.PremiumOverlayVisibility => false,
            _ => true
        };

        private static ProfileSettings ProfileConfigTypeToProfileSettings(ProfileConfigType type) => type switch
        {
            ProfileConfigType.Bar                      => ProfileSettings.BarOnTop,
            ProfileConfigType.MiniFavCard              => ProfileSettings.ShowWaifu,
            ProfileConfigType.AnimeStats               => ProfileSettings.ShowAnime,
            ProfileConfigType.MangaStats               => ProfileSettings.ShowManga,
            ProfileConfigType.CardsStats               => ProfileSettings.ShowCards,
            ProfileConfigType.MiniGallery              => ProfileSettings.ShowGallery,
            ProfileConfigType.CardCntInMiniGallery     => ProfileSettings.HalfGallery,
            ProfileConfigType.FlipPanels               => ProfileSettings.Flip,
            ProfileConfigType.LevelAvatarBorder        => ProfileSettings.BorderColor,
            ProfileConfigType.RoundAvatarWithoutBorder => ProfileSettings.RoundAvatar,
            ProfileConfigType.CustomBarOpacity         => ProfileSettings.BarOpacity,
            ProfileConfigType.OverlayVisibility        => ProfileSettings.ShowOverlay,
            ProfileConfigType.PremiumOverlayVisibility => ProfileSettings.ShowOverlayPro,
            _ => ProfileSettings.None
        };

        private NamePair<ProfileConfigType> GetSelectedOption(string input, uint index, bool useIndex) => useIndex
                ? _profileConfigTypes.FirstOrDefault(x => x.Index == index)
                : _profileConfigTypes.FirstOrDefault(x => input.StartsWith(x.Name, StringComparison.OrdinalIgnoreCase));

        private NamePair<ProfileType> GetSelectedStyle(string input, uint index, bool useIndex) => useIndex
                ? _profileStyleTypes.FirstOrDefault(x => x.Index == index)
                : _profileStyleTypes.FirstOrDefault(x => input.StartsWith(x.Name, StringComparison.OrdinalIgnoreCase));

        private NamePair<AvatarBorder> GetSelectedAvatarBorder(string input, uint index, bool useIndex) => useIndex
                ? _avatarBorderTypes.FirstOrDefault(x => x.Index == index)
                : _avatarBorderTypes.FirstOrDefault(x => input.StartsWith(x.Name, StringComparison.OrdinalIgnoreCase));

        public override Task<TypeReaderResult> ReadAsync(ICommandContext context, string input, IServiceProvider services)
        {
            var config = new ProfileConfig { Currency = CurrencyType.SC, Type = ProfileConfigType.ShowInfo, ToggleCurentValue = false };
            if (!string.IsNullOrEmpty(input))
            {
                var param = input.Split(' ').Where(x => !string.IsNullOrEmpty(x)).ToArray();
                if (param.Length < 1)
                    return Task.FromResult(TypeReaderResult.FromError(CommandError.ParseFailed, "Nie podano wymaganych parametrów!"));

                var globalParamIndex = 0;
                var isIndex = uint.TryParse(param[globalParamIndex], out var mainIndex);
                var selectedType = GetSelectedOption(input, mainIndex, isIndex);
                if (selectedType is null)
                {
                    return Task.FromResult(TypeReaderResult.FromError(CommandError.ParseFailed, "Nie rozpoznano typu konfiguracji!"));
                }

                config.Type = selectedType.Type;
                var strippedInput = input.AsSpan().Slice(isIndex ? param[globalParamIndex].Length : selectedType.Name.Length);
                globalParamIndex += isIndex ? 1 : (selectedType.Name.Count(x => Char.IsSeparator(x)) + 1);
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
                    case ProfileConfigType.PremiumOverlay:
                    case ProfileConfigType.Background:
                    {
                        var spaceIdx = strippedInput.IndexOf(' ');
                        var url = spaceIdx == -1 ? strippedInput : strippedInput.Slice(0, spaceIdx);
                        strippedInput = spaceIdx == -1 ? Span<char>.Empty : strippedInput.Slice(spaceIdx);

                        config.Url = url.Trim().ToString();
                        if (config.Url.Equals("att", StringComparison.OrdinalIgnoreCase))
                        {
                            if (context.Message.Attachments.IsNullOrEmpty())
                            {
                                return Task.FromResult(TypeReaderResult.FromError(CommandError.ParseFailed, "Nie wykrytko załącznika!"));
                            }
                            config.Url = context.Message.Attachments.FirstOrDefault()?.Url ?? "";
                        }

                        if (!Uri.IsWellFormedUriString(config.Url, UriKind.Absolute))
                        {
                            return Task.FromResult(TypeReaderResult.FromError(CommandError.ParseFailed, "Nie rozpoznano linku!"));
                        }
                        globalParamIndex++;
                    }
                    break;

                    case ProfileConfigType.BackgroundAndStyle:
                    case ProfileConfigType.Style:
                    {
                        input = strippedInput.ToString();
                        isIndex = uint.TryParse(param[globalParamIndex], out var styleIndex);
                        var selectedStyleType = GetSelectedStyle(input, styleIndex, isIndex);
                        if (selectedStyleType is null)
                        {
                            return Task.FromResult(TypeReaderResult.FromError(CommandError.ParseFailed, "Nie rozpoznano stylu profilu!"));
                        }

                        config.Style = selectedStyleType.Type;
                        strippedInput = strippedInput.Slice(isIndex ? param[globalParamIndex].Length : selectedStyleType.Name.Length);
                        globalParamIndex += isIndex ? 1 : (selectedStyleType.Name.Count(x => Char.IsSeparator(x)) + 1);

                        if (selectedStyleType.StrictMatching && !isIndex)
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
                    case ProfileConfigType.RoundAvatarWithoutBorder:
                    case ProfileConfigType.CustomBarOpacity:
                    case ProfileConfigType.OverlayVisibility:
                    case ProfileConfigType.PremiumOverlayVisibility:
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
                        globalParamIndex++;
                    }
                    break;

                    case ProfileConfigType.AvatarBorder:
                    {
                        input = strippedInput.ToString();
                        isIndex = uint.TryParse(param[globalParamIndex], out var avatarIndex);
                        var selectedAvatarType = GetSelectedAvatarBorder(input, avatarIndex, isIndex);
                        if (selectedAvatarType is null)
                        {
                            return Task.FromResult(TypeReaderResult.FromError(CommandError.ParseFailed, "Nie rozpoznano ramki awatara!"));
                        }

                        config.Border = selectedAvatarType.Type;
                        strippedInput = strippedInput.Slice(isIndex ? param[globalParamIndex].Length : selectedAvatarType.Name.Length);
                        globalParamIndex += isIndex ? 1 : (selectedAvatarType.Name.Count(x => Char.IsSeparator(x)) + 1);
                    }
                    break;

                    default:
                        return Task.FromResult(TypeReaderResult.FromError(CommandError.ParseFailed, "Nie rozpoznano typu konfiguracji!"));
                }

                if (!strippedInput.IsEmpty)
                {
                     globalParamIndex += ParseCurrency(strippedInput.TrimStart(), out config.Currency) ? 1 : 0;
                }

                if (param.Length - globalParamIndex > 0)
                {
                    return Task.FromResult(TypeReaderResult.FromError(CommandError.ParseFailed, "Podano za dużo parametrów do wybranej opcji!"));
                }
            }
            return Task.FromResult(TypeReaderResult.FromSuccess(config));
        }
    }
}