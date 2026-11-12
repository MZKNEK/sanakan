using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Sanakan.Database.Models;
using Sanakan.Extensions;
using Sanakan.Services;
using Sanakan.Services.PocketWaifu;
using Sanakan.Services.ScamImages;
using Sanakan.ShindenCompat.Models;
using ListStats = Sanakan.ShindenCompat.Models.Entities.ListStats;
using SeriesStatus = Sanakan.ShindenCompat.Models.Entities.SeriesStatus;
using DedicatedTime = Sanakan.ShindenCompat.Models.Entities.DedicatedTime;
using MeanScore = Sanakan.ShindenCompat.Models.Entities.MeanScore;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Artifacts
{
    // Renders a fixed set of pictures for comparing ImageSharp versions pixel by pixel.
    // Runs only when SANAKAN_GOLDEN_IN (input pictures) and SANAKAN_GOLDEN_OUT are set.
    public class ImageGoldenRender
    {
        private static readonly DateTime Fixed = new DateTime(2024, 5, 1, 12, 0, 0);

        private sealed class ShindenUser : IUserInfo
        {
            public ulong Id { get; set; }
            public string Name { get; set; }
            public string AvatarUrl { get; set; }
            public string Rank { get; set; }
            public ulong? ForumId { get; set; }
            public IListStats ListStats { get; set; }
        }

        private sealed class Watched : ILastWatched
        {
            public ulong TitleId { get; set; }
            public long EpisodeNo { get; set; }
            public long EpisodesCnt { get; set; }
            public string AnimeTitle { get; set; }
            public string AnimeCoverUrl { get; set; }
        }

        private sealed class Readed : ILastReaded
        {
            public ulong TitleId { get; set; }
            public decimal ChapterNo { get; set; }
            public long ChaptersCnt { get; set; }
            public string MangaTitle { get; set; }
            public string MangaCoverUrl { get; set; }
        }

        [Fact]
        public async Task RenderAll()
        {
            var input = Environment.GetEnvironmentVariable("SANAKAN_GOLDEN_IN");
            var output = Environment.GetEnvironmentVariable("SANAKAN_GOLDEN_OUT");
            if (string.IsNullOrEmpty(input) || string.IsNullOrEmpty(output))
                return;

            Directory.CreateDirectory(output);
            using var server = new LocalHttpServer(async ctx =>
            {
                var path = Path.Combine(input, ctx.Request.Url.AbsolutePath.TrimStart('/'));
                if (!File.Exists(path))
                {
                    ctx.Response.StatusCode = 404;
                    ctx.Response.Close();
                    return;
                }
                var body = await File.ReadAllBytesAsync(path);
                ctx.Response.ContentType = Path.GetExtension(path) switch
                {
                    ".jpg" => "image/jpeg",
                    ".gif" => "image/gif",
                    ".webp" => "image/webp",
                    _ => "image/png",
                };
                ctx.Response.ContentLength64 = body.Length;
                await ctx.Response.OutputStream.WriteAsync(body);
                ctx.Response.Close();
            });
            string U(string file) => server.BaseUrl + file;

            var img = new ImageProcessing(null, new TagIcon(7, "galeria", "x"), true);
            var log = new StringBuilder();

            async Task Render(string name, Func<Task<Image>> render)
            {
                try
                {
                    using var image = await render();
                    Dump(image, output, name);
                    log.AppendLine($"{name}: {image.Width}x{image.Height} frames {image.Frames.Count}");
                }
                catch (Exception ex)
                {
                    log.AppendLine($"{name}: FAILED {ex.GetType().Name}: {ex.Message}");
                }
            }

            ulong nextId = 1;
            Card NewCard(Rarity rarity, Dere dere, string picture = "char.png")
            {
                var id = nextId++;
                return new Card
                {
                    Id = id,
                    Name = $"Postać numer {id} Łódź",
                    Title = "Przykładowy tytuł anime",
                    Character = 1000 + id,
                    Rarity = rarity,
                    RarityOnStart = rarity,
                    Dere = dere,
                    Attack = 40 + (int)id * 7,
                    Defence = 10 + (int)id * 3,
                    Health = 300 + (int)id * 11,
                    Affection = id % 3 == 0 ? -5 : 4.5,
                    ExpCnt = id * 1.5,
                    UpgradesCnt = (int)(id % 3),
                    Image = picture == null ? null : U(picture),
                    CreationDate = Fixed,
                    Tags = new List<Tag>(),
                    Relation = new List<TagCardRelation>(),
                };
            }

            var cards = new List<(string name, Card card)>();
            var deres = Enum.GetValues<Dere>();
            int di = 0;
            foreach (var rarity in Enum.GetValues<Rarity>())
                cards.Add(($"rarity_{rarity}", NewCard(rarity, deres[di++ % deres.Length])));

            foreach (var dere in deres)
                cards.Add(($"dere_{dere}", NewCard(Rarity.A, dere)));

            foreach (var quality in Enum.GetValues<Quality>().Where(x => x != Quality.Broken))
            {
                var c = NewCard(Rarity.SSS, Dere.Yato);
                c.FromFigure = true;
                c.Quality = quality;
                c.QualityOnStart = quality;
                c.AttackBonus = 120;
                c.HealthBonus = 450;
                c.DefenceBonus = 30;
                cards.Add(($"ultimate_{quality}", c));
            }

            var jpg = NewCard(Rarity.S, Dere.Kuudere, "char.jpg");
            cards.Add(("picture_jpg", jpg));
            var wide = NewCard(Rarity.SS, Dere.Deredere, "wide.png");
            cards.Add(("picture_wide", wide));
            var custom = NewCard(Rarity.SSS, Dere.Tsundere);
            custom.CustomImage = U("custom.webp");
            cards.Add(("picture_custom_webp", custom));
            var missing = NewCard(Rarity.B, Dere.Bodere, "missing.png");
            cards.Add(("picture_missing", missing));
            var noPicture = NewCard(Rarity.C, Dere.Mayadere, null);
            cards.Add(("picture_none", noPicture));
            var border = NewCard(Rarity.SSS, Dere.Kamidere);
            border.CustomBorder = U("border.png");
            cards.Add(("custom_border", border));
            var restarted = NewCard(Rarity.SSS, Dere.Yandere);
            restarted.RestartCnt = 3;
            restarted.StarStyle = StarStyle.Empty;
            cards.Add(("restarts_empty_stars", restarted));
            var restartedFull = NewCard(Rarity.SSS, Dere.Raito);
            restartedFull.RestartCnt = 12;
            cards.Add(("restarts_full_stars", restartedFull));
            var unique = NewCard(Rarity.SS, Dere.Dandere);
            unique.Unique = true;
            cards.Add(("unique", unique));
            foreach (var curse in Enum.GetValues<CardCurse>().Where(x => x != CardCurse.None))
            {
                var c = NewCard(Rarity.A, Dere.Yami);
                c.Curse = curse;
                cards.Add(($"curse_{curse}", c));
            }
            var animated = NewCard(Rarity.SS, Dere.Tsundere, "anim.gif");
            animated.IsAnimatedImage = true;
            cards.Add(("animated", animated));
            var animatedUlt = NewCard(Rarity.SSS, Dere.Yato, "anim.gif");
            animatedUlt.IsAnimatedImage = true;
            animatedUlt.FromFigure = true;
            animatedUlt.Quality = Quality.Lambda;
            cards.Add(("animated_ultimate", animatedUlt));

            foreach (var (name, card) in cards)
            {
                await Render($"card_{name}", () => img.GetWaifuCardAsync(card));
                await Render($"profilecard_{name}", () => img.GetWaifuInProfileCardAsync(card));
            }

            var stats = new ListStats
            {
                AnimeStatus = new SeriesStatus { InProgress = 12, Completed = 340, Dropped = 7, Skipped = 3, InPlan = 120, OnHold = 9 },
                MangaStatus = new SeriesStatus { InProgress = 4, Completed = 77, Dropped = 2, Skipped = 0, InPlan = 31, OnHold = 1 },
                EpisodesCount = 9876,
                ChaptersCount = 4321,
                AnimeTime = new DedicatedTime { Years = 0, Months = 3, Days = 12, Hours = 5, Minutes = 40 },
                MangaTime = new DedicatedTime { Years = 0, Months = 1, Days = 2, Hours = 7, Minutes = 3 },
                AnimeMeanScore = new MeanScore { Count = 300, Rating = 7.42 },
                MangaMeanScore = new MeanScore { Count = 60, Rating = 8.1 },
            };
            var shinden = new ShindenUser { Id = 5, Name = "gracz", Rank = "Ranga testowa", AvatarUrl = U("avatar.png"), ListStats = stats };

            User NewUser(ProfileType type, ProfileSettings settings, AvatarBorder avatarBorder)
            {
                var user = new User().Default(900 + nextId++, Fixed);
                user.Level = 37;
                user.ExpCnt = 123456;
                user.ScCnt = 98765;
                user.TcCnt = 4321;
                user.ProfileType = type;
                user.StatsStyleSettings = settings;
                user.AvatarBorder = avatarBorder;
                user.BackgroundProfileUri = U("bg.jpg");
                user.StatsReplacementProfileUri = U("stats.png");
                user.CustomProfileOverlayUrl = U("overlay.png");
                user.PremiumCustomProfileOverlayUrl = U("overlaypro.png");
                foreach (var (_, card) in cards.Where(x => !x.card.IsAnimatedImage).Take(14))
                {
                    var copy = NewCard(card.Rarity, card.Dere);
                    copy.GameDeckId = user.Id;
                    if (copy.Id % 3 != 0)
                        copy.Tags.Add(new Tag { Id = 7, Name = "galeria" });
                    user.GameDeck.Cards.Add(copy);
                }
                user.GameDeck.Waifu = user.GameDeck.Cards.First().Character;
                return user;
            }

            var profiles = new List<(string, ProfileType, ProfileSettings, AvatarBorder)>
            {
                ("stats_default", ProfileType.Stats, ProfileSettings.Default, AvatarBorder.Base),
                ("stats_flip_waifu_round", ProfileType.Stats, ProfileSettings.Default | ProfileSettings.Flip | ProfileSettings.ShowWaifu | ProfileSettings.RoundAvatar, AvatarBorder.None),
                ("img", ProfileType.Img, ProfileSettings.Default, AvatarBorder.Fire),
                ("stats_with_img", ProfileType.StatsWithImg, ProfileSettings.Default, AvatarBorder.Gold),
                ("cards", ProfileType.Cards, ProfileSettings.Default | ProfileSettings.BarOnTop, AvatarBorder.Ice),
                ("cards_on_img", ProfileType.CardsOnImg, ProfileSettings.Default | ProfileSettings.BarOpacity, AvatarBorder.Rainbow),
                ("stats_on_img", ProfileType.StatsOnImg, ProfileSettings.Default, AvatarBorder.Water),
                ("mini_gallery", ProfileType.MiniGallery, ProfileSettings.Default, AvatarBorder.Skull),
                ("mini_gallery_half_on_img", ProfileType.MiniGalleryOnImg, ProfileSettings.Default | ProfileSettings.HalfGallery, AvatarBorder.PurpleLeaves),
                ("no_overlays", ProfileType.Stats, ProfileSettings.ShowAnime | ProfileSettings.ShowCards, AvatarBorder.None),
            };
            foreach (var (name, type, settings, avBorder) in profiles)
            {
                var user = NewUser(type, settings, avBorder);
                await Render($"profile_{name}", () => img.GetUserProfileAsync(shinden, user, U("avatar.png"), 17, "Bardzo długi nick gracza Żółć", new Discord.Color(0x9B59B6)));
            }
            var animatedAvatarUser = NewUser(ProfileType.Stats, ProfileSettings.Default, AvatarBorder.Bow);
            await Render("profile_gif_avatar_no_shinden", () => img.GetUserProfileAsync(null, animatedAvatarUser, U("avatar.gif"), 1, "krótki", Discord.Color.Default));

            await Render("badge_png", async () => await img.GetLevelUpBadgeAsync("gracz", 42, U("avatar.png"), new Discord.Color(30, 200, 30)));
            await Render("badge_gif_highlight", async () => await img.GetLevelUpBadgeAsync("Bardzo Długi Nick Gracza", 1234, U("avatar.gif"), Discord.Color.Default, new Discord.Color(0xE67E22)));
            await Render("badge_missing_avatar", async () => await img.GetLevelUpBadgeAsync("ąęść", 7, U("missing.png"), new Discord.Color(0x3498DB)));

            var watched = new List<ILastWatched>
            {
                new Watched { TitleId = 1, AnimeTitle = "Pierwsze anime o bardzo długim tytule", EpisodeNo = 5, EpisodesCnt = 12, AnimeCoverUrl = U("cover1.jpg") },
                new Watched { TitleId = 2, AnimeTitle = "Drugie", EpisodeNo = 100, EpisodesCnt = 0, AnimeCoverUrl = U("cover2.png") },
                new Watched { TitleId = 3, AnimeTitle = "Trzecie", EpisodeNo = 1, EpisodesCnt = 1, AnimeCoverUrl = U("missing.jpg") },
            };
            var readed = new List<ILastReaded>
            {
                new Readed { TitleId = 4, MangaTitle = "Manga pierwsza", ChapterNo = 12.5m, ChaptersCnt = 40, MangaCoverUrl = U("cover2.png") },
                new Readed { TitleId = 5, MangaTitle = "Manga druga z długim tytułem", ChapterNo = 3, ChaptersCnt = 0, MangaCoverUrl = U("cover1.jpg") },
            };
            await Render("sitestats_full", async () => await img.GetSiteStatisticAsync(shinden, new Discord.Color(0x1ABC9C), readed, watched));
            await Render("sitestats_empty", async () => await img.GetSiteStatisticAsync(new ShindenUser { Id = 6, Name = "pusty", AvatarUrl = U("avatar.png") }, Discord.Color.Default));

            foreach (var currency in Enum.GetValues<CurrencyType>())
                await Render($"fcolors_{currency}", () => Task.FromResult<Image>(img.GetFColorsView(currency)));

            foreach (var side in Enum.GetValues<DuelInfo.WinnerSide>())
            {
                await Render($"duel_{side}", async () =>
                {
                    using var win = (await img.GetWaifuCardAsync(cards[0].card)).CloneAs<Rgba32>();
                    using var los = (await img.GetWaifuCardAsync(cards[3].card)).CloneAs<Rgba32>();
                    var info = new DuelInfo { Side = side, Winner = cards[0].card, Loser = cards[3].card };
                    return img.GetDuelCardImage(info, null, win, los);
                });
            }

            await Render("safari", async () =>
            {
                using var card = await img.GetWaifuCardAsync(cards[1].card);
                return img.GetCatchThatWaifuImage(card, SafariImage.DefaultUri(SafariImage.Type.Truth), SafariImage.DefaultX(), SafariImage.DefaultY());
            });

            if (Environment.GetEnvironmentVariable("SANAKAN_GOLDEN_BENCH") == "1")
            {
                var bench = new StringBuilder();
                var benchUser = NewUser(ProfileType.Stats, ProfileSettings.Default | ProfileSettings.ShowWaifu, AvatarBorder.Base);
                var benches = new List<(string, Func<Task<Image>>)>
                {
                    ("card", () => img.GetWaifuCardAsync(cards[0].card)),
                    ("card_ultimate_Alpha", () => img.GetWaifuCardAsync(cards.First(x => x.name == "ultimate_Alpha").card)),
                    ("profilecard", () => img.GetWaifuInProfileCardAsync(cards[0].card)),
                    ("profile", () => img.GetUserProfileAsync(shinden, benchUser, U("avatar.png"), 17, "gracz", new Discord.Color(0x9B59B6))),
                    ("badge", async () => await img.GetLevelUpBadgeAsync("gracz", 42, U("avatar.png"), new Discord.Color(30, 200, 30))),
                    ("sitestats", async () => await img.GetSiteStatisticAsync(shinden, new Discord.Color(0x1ABC9C), readed, watched)),
                };
                foreach (var (name, render) in benches)
                {
                    for (int i = 0; i < 3; i++)
                        (await render()).Dispose();
                    var times = new List<double>();
                    for (int i = 0; i < 25; i++)
                    {
                        var sw = System.Diagnostics.Stopwatch.StartNew();
                        using (var image = await render())
                        using (var webp = image.ToWebpStream()) { }
                        times.Add(sw.Elapsed.TotalMilliseconds);
                    }
                    times.Sort();
                    bench.AppendLine($"{name}: median {times[times.Count / 2]:0.0} ms, min {times[0]:0.0} ms");
                }
                File.WriteAllText(Path.Combine(output, "bench.txt"), bench.ToString());
            }

            var jpegs = Directory.GetFiles(input, "*.jpg").Concat(Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Pictures", "PW"), "*.jpg"));
            foreach (var jpeg in jpegs)
            {
                using var decoded = Image.Load<Rgba32>(jpeg);
                Dump(decoded, output, $"decode_{Path.GetFileNameWithoutExtension(jpeg)}");
            }

            var hashes = new StringBuilder();
            foreach (var file in Directory.GetFiles(input).Concat(Directory.GetFiles(output, "*.webp")).Concat(Directory.GetFiles(output, "*.gif")).OrderBy(x => Path.GetFileName(x)))
            {
                using var stream = File.OpenRead(file);
                var hash = PerceptualHash.IsAnimated(stream) ? "animated" : string.Join(" ", PerceptualHash.FromStreamOrientations(stream).Select(x => x.ToString("x16")));
                hashes.AppendLine($"{Path.GetFileName(file)} {hash}");
            }
            File.WriteAllText(Path.Combine(output, "phash.txt"), hashes.ToString());
            File.WriteAllText(Path.Combine(output, "render.log"), log.ToString());
        }

        private static void Dump(Image image, string dir, string name)
        {
            using var rgba = image.CloneAs<Rgba32>();
            var meta = new StringBuilder($"{rgba.Width}x{rgba.Height} frames={rgba.Frames.Count}\n");
            for (int f = 0; f < rgba.Frames.Count; f++)
            {
                using var frame = rgba.Frames.CloneFrame(f);
                var bytes = new byte[frame.Width * frame.Height * 4];
                frame.CopyPixelDataTo(bytes);
                using var file = File.Create(Path.Combine(dir, $"{name}.f{f}.rgba"));
                file.Write(BitConverter.GetBytes(frame.Width));
                file.Write(BitConverter.GetBytes(frame.Height));
                file.Write(bytes);
                meta.AppendLine($"frame {f} delay={rgba.Frames[f].Metadata.GetWebpMetadata().FrameDelay}");
            }
            File.WriteAllText(Path.Combine(dir, $"{name}.meta.txt"), meta.ToString());

            using (var webp = image.ToWebpStream())
            using (var target = File.Create(Path.Combine(dir, $"{name}.webp")))
                webp.CopyTo(target);

            if (image.Frames.Count > 1)
            {
                using var gif = image.ToGifStream();
                using var target = File.Create(Path.Combine(dir, $"{name}.gif"));
                gif.CopyTo(target);
            }
        }
    }
}
