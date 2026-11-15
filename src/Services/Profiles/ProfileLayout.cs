#pragma warning disable 1591

using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace Sanakan.Services.Profiles
{
    public enum PanelLook { Solid, Glass, Shadow }

    public enum BarPlace { Bottom, Top, Hidden }

    public enum AvatarShape { Circle, Rounded, Square }

    public enum NickAlign { Left, Center }

    public enum ListPanelSize { Small, Large }

    public enum LegendMode { Main, All }

    // what darkens the header under the nick: the old profile's band, a fade or nothing
    public enum HeaderShade { Fade, Band, None }

    // what lies under a card-sticker
    public enum StickerGround { Panel, Glass, Shadow, None }

    public enum BarLook { Solid, Glass }

    // the card game's panel: full, narrow (the waifu alone, or the numbers one under another) or low (the numbers in rows)
    public enum CardsPanelSize { Full, Narrow, Low }

    // The look of a profile: fixed parts (header and style images, avatar, nick, ultra overlay, bar)
    // and free layers drawn between the nick and the ultra overlay, bottom to top.
    // Stored as JSON; unknown fields and layer types are skipped, so newer layouts still load.
    public class ProfileLayout
    {
        public const int CurrentVersion = 1;
        public const int Width = 750;
        public const int Height = 500;

        [JsonProperty("v")] public int Version { get; set; } = CurrentVersion;
        // "#rrggbb", or null for the colour of the player's role
        [JsonProperty("accent")] public string Accent { get; set; }
        [JsonProperty("panels")] public PanelLook Panels { get; set; } = PanelLook.Solid;
        [JsonProperty("shadow")] public int ShadowOpacity { get; set; } = 70;
        [JsonProperty("shade")] public HeaderShade Shade { get; set; } = HeaderShade.Fade;
        // drawn at twice the size, 1500 x 1000; the layout itself stays in 750 x 500
        [JsonProperty("hd")] public bool Hd { get; set; }
        [JsonProperty("bar")] public BarLayout Bar { get; set; } = new BarLayout();
        [JsonProperty("header")] public SlotImage Header { get; set; }
        [JsonProperty("style")] public SlotImage Style { get; set; }
        [JsonProperty("avatar")] public AvatarLayer Avatar { get; set; } = new AvatarLayer();
        [JsonProperty("nick")] public NickLayer Nick { get; set; } = new NickLayer();
        [JsonProperty("ultra")] public UltraLayer Ultra { get; set; }
        [JsonProperty("layers")] public List<ProfileLayer> Layers { get; set; } = new List<ProfileLayer>();
        // what moves: "avatar", "header", "style", "ultra", "frame", "layer:<id>", or null
        [JsonProperty("animate")] public string Animate { get; set; }

        public const string AnimateAvatar = "avatar";
        public const string AnimateHeader = "header";
        public const string AnimateStyle = "style";
        public const string AnimateUltra = "ultra";
        public const string AnimateFrame = "frame";
        public const string AnimateLayerPrefix = "layer:";

        public static string AnimateLayer(int id) => AnimateLayerPrefix + id;

        public IEnumerable<T> LayersOf<T>() where T : ProfileLayer => Layers.OfType<T>();

        public ProfileLayer FindLayer(int id) => Layers.FirstOrDefault(x => x.Id == id);

        public int NextLayerId() => Layers.Count == 0 ? 1 : Layers.Max(x => x.Id) + 1;

        public static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
            MissingMemberHandling = MissingMemberHandling.Ignore,
            ContractResolver = new DefaultContractResolver(),
            Converters = { new StringEnumConverter(new CamelCaseNamingStrategy()), new ProfileLayerConverter() },
        };

        public string ToJson() => JsonConvert.SerializeObject(this, JsonSettings);

        public static ProfileLayout FromJson(string json)
        {
            var layout = JsonConvert.DeserializeObject<ProfileLayout>(json, JsonSettings) ?? new ProfileLayout();
            layout.Layers = (layout.Layers ?? new List<ProfileLayer>()).Where(x => x != null).ToList();
            layout.Bar ??= new BarLayout();
            layout.Avatar ??= new AvatarLayer();
            layout.Nick ??= new NickLayer();
            return layout;
        }

        public ProfileLayout Clone() => FromJson(ToJson());
    }

    public class BarLayout
    {
        public const string Top = "top";
        public const string Level = "level";
        public const string Exp = "exp";
        public const string Messages = "msgs";
        public const string Sc = "sc";
        public const string Tc = "tc";
        public const string Ac = "ac";
        public const string Ct = "ct";
        public const string Pc = "pc";

        public static readonly string[] Items = { Top, Level, Exp, Messages, Sc, Tc, Ac, Ct, Pc };

        [JsonProperty("at")] public BarPlace At { get; set; } = BarPlace.Bottom;
        [JsonProperty("opacity")] public int Opacity { get; set; } = 85;
        [JsonProperty("look")] public BarLook Look { get; set; } = BarLook.Solid;
        [JsonProperty("hide")] public List<string> Hide { get; set; } = new List<string>();

        public bool Shows(string item) => Hide == null || !Hide.Contains(item);
    }

    // the header or the style image: always covers its field, moved and zoomed inside it
    public class SlotImage
    {
        [JsonProperty("image")] public long Image { get; set; }
        [JsonProperty("x")] public int X { get; set; }
        [JsonProperty("y")] public int Y { get; set; }
        [JsonProperty("scale")] public double Scale { get; set; } = 1;
        [JsonProperty("hidden")] public bool Hidden { get; set; }
    }

    public abstract class ImageLayerBase : ProfileLayer
    {
        [JsonProperty("image")] public long Image { get; set; }
        [JsonProperty("scale")] public double Scale { get; set; } = 1;
        [JsonProperty("opacity")] public double Opacity { get; set; } = 1;
    }

    // drawn over every free layer, anywhere on the profile
    public class UltraLayer : ImageLayerBase
    {
        public override string Type => "ultra";
    }

    public class AvatarLayer
    {
        public const string NoFrame = "None";
        public const string CustomFrame = "custom";

        [JsonProperty("x")] public int X { get; set; } = 52;
        [JsonProperty("y")] public int Y { get; set; } = 68;
        [JsonProperty("size")] public int Size { get; set; } = 84;
        [JsonProperty("shape")] public AvatarShape Shape { get; set; } = AvatarShape.Circle;
        // a border of the bot by its AvatarBorder name, "custom" for the player's picture, "None" for none
        [JsonProperty("frame")] public string Frame { get; set; } = nameof(Database.Models.AvatarBorder.Base);
        // the border's look for the player's level, where the border has one
        [JsonProperty("levelFrame")] public bool LevelFrame { get; set; } = true;
        [JsonProperty("customImage")] public long CustomImage { get; set; }
        [JsonProperty("customScale")] public double CustomScale { get; set; } = 1;
        [JsonProperty("customX")] public int CustomX { get; set; }
        [JsonProperty("customY")] public int CustomY { get; set; }

        [JsonIgnore] public bool HasFrame => !string.IsNullOrEmpty(Frame) && Frame != NoFrame;
        [JsonIgnore] public bool HasCustomFrame => Frame == CustomFrame;
    }

    public class NickLayer
    {
        public const int Height = 34;

        [JsonProperty("x")] public int X { get; set; } = 163;
        [JsonProperty("y")] public int Y { get; set; } = 99;
        [JsonProperty("w")] public int W { get; set; } = 445;
        [JsonProperty("align")] public NickAlign Align { get; set; } = NickAlign.Left;
        [JsonProperty("hidden")] public bool Hidden { get; set; }
    }

    public abstract class ProfileLayer
    {
        [JsonProperty("t", Order = -2)] public abstract string Type { get; }
        [JsonProperty("id", Order = -1)] public int Id { get; set; }
        [JsonProperty("x")] public int X { get; set; }
        [JsonProperty("y")] public int Y { get; set; }
        [JsonProperty("hidden")] public bool Hidden { get; set; }
    }

    public class ListPanelLayer : ProfileLayer
    {
        public const string Mean = "mean";
        public const string Numbers = "numbers";
        public const string StatusBar = "bar";
        public const string LegendRow = "legend";
        public static readonly string[] Parts = { Mean, Numbers, StatusBar, LegendRow };

        public const int MaxRecent = 3;

        [JsonIgnore] public bool Manga { get; set; }
        [JsonProperty("size")] public ListPanelSize Size { get; set; } = ListPanelSize.Small;
        [JsonProperty("legend")] public LegendMode Legend { get; set; } = LegendMode.Main;
        // how many of the recently watched or read titles to show, 0 for none
        [JsonProperty("recent")] public int Recent { get; set; }
        [JsonProperty("hide")] public List<string> Hide { get; set; } = new List<string>();

        public override string Type => Manga ? "manga" : "anime";

        public bool Shows(string part) => Hide == null || !Hide.Contains(part);
    }

    public class CardsPanelLayer : ProfileLayer
    {
        public const string Karma = "karma";
        public const string Waifu = "waifu";
        public const string Count = "count";
        public const string Rarities = "rarity";
        public const string Ultimates = "ult";
        public const string CustomImages = "scal";
        public const string CustomBorders = "scis";
        public static readonly string[] Parts = { Karma, Waifu, Count, Rarities, Ultimates, CustomImages, CustomBorders };

        [JsonProperty("hide")] public List<string> Hide { get; set; } = new List<string>();
        // the panel's ground, off for the card game alone on the picture
        [JsonProperty("panel")] public bool Panel { get; set; } = true;
        [JsonProperty("size")] public CardsPanelSize Size { get; set; } = CardsPanelSize.Full;

        public override string Type => "cards";

        public bool Shows(string part) => Hide == null || !Hide.Contains(part);
    }

    // the cards of the profile gallery: 6 small or 2 big in a panel
    public class GalleryLayer : ProfileLayer
    {
        [JsonProperty("count")] public int Count { get; set; } = 6;
        // the panel's ground under the cards, off for the cards alone on the picture
        [JsonProperty("panel")] public bool Panel { get; set; } = true;

        public override string Type => "gallery";
    }

    // twelve cards of the profile gallery in two rows
    public class BigGalleryLayer : ProfileLayer
    {
        // 12 in two rows, 6 in one, 3 in half of one; the panel fits them
        [JsonProperty("count")] public int Count { get; set; } = 12;
        [JsonProperty("panel")] public bool Panel { get; set; } = true;

        public override string Type => "bigGallery";
    }

    public class OverlayLayer : ImageLayerBase
    {
        public override string Type => "overlay";
    }

    // one of the player's cards placed freely
    public class StickerLayer : ProfileLayer
    {
        public const int MinHeight = 60;
        public const int MaxHeight = 300;
        public const int MaxTilt = 15;
        public const int MaxPad = 40;

        private StickerGround _ground = StickerGround.Panel;
        private bool _groundSet;

        [JsonProperty("card")] public ulong Card { get; set; }
        [JsonProperty("h")] public int H { get; set; } = 140;
        [JsonProperty("tilt")] public int Tilt { get; set; }
        [JsonProperty("ground")] public StickerGround Ground { get => _ground; set { _ground = value; _groundSet = true; } }
        // 0-100, or null for the kind's own: 45 for the panel, 55 for the glass's tint, 50 for the shadow
        [JsonProperty("groundOpacity")] public int? GroundOpacity { get; set; }
        // how far the panel or glass reaches past the card
        [JsonProperty("pad")] public int Pad { get; set; } = 4;

        // layouts saved before the ground had kinds: "bg": false was the shadow
        [JsonProperty("bg")]
        private bool? LegacyBackground
        {
            get => null;
            set { if (value == false && !_groundSet) _ground = StickerGround.Shadow; }
        }

        public float GroundAlpha => Math.Clamp(GroundOpacity ?? (Ground switch { StickerGround.Glass => 55, StickerGround.Shadow => 50, _ => 45 }), 0, 100) / 100f;

        public override string Type => "sticker";
    }

    public class RankLayer : ProfileLayer
    {
        // centred: x is the middle of the pill
        [JsonProperty("align")] public NickAlign Align { get; set; } = NickAlign.Left;

        public override string Type => "rank";
    }

    // reads a layer by its "t"; an unknown type reads as null and is dropped by ProfileLayout.FromJson
    public class ProfileLayerConverter : JsonConverter
    {
        public override bool CanWrite => false;

        public override bool CanConvert(Type objectType) => objectType == typeof(ProfileLayer);

        public static ProfileLayer Create(string type) => type switch
        {
            "anime" => new ListPanelLayer(),
            "manga" => new ListPanelLayer { Manga = true },
            "cards" => new CardsPanelLayer(),
            "gallery" => new GalleryLayer(),
            "bigGallery" => new BigGalleryLayer(),
            "overlay" => new OverlayLayer(),
            "sticker" => new StickerLayer(),
            "rank" => new RankLayer(),
            _ => null,
        };

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null)
                return null;

            var obj = JObject.Load(reader);
            var layer = Create(obj.Value<string>("t"));
            if (layer == null)
                return null;

            using (var sub = obj.CreateReader())
                serializer.Populate(sub, layer);

            if (layer is ListPanelLayer list)
                list.Manga = obj.Value<string>("t") == "manga";

            return layer;
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer) => throw new NotSupportedException();
    }
}
