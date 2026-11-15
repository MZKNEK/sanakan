#pragma warning disable 1591

using System;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Sanakan.Config.Model
{
    [Flags]
    public enum ApiAppPermission
    {
        None = 0,
        UserKeys = 1,
        // what the site (sansite) reads and does for its logged-in accounts: permissions, profiles, Profilator
        Sansite = 2,
        Site = 4,
    }

    // the names of ApiAppPermission, also the old "Info" of Sansite in configs written before
    public class ApiAppPermissionConverter : StringEnumConverter
    {
        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.String)
            {
                var names = ((string)reader.Value).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(x => x.Equals("Info", StringComparison.OrdinalIgnoreCase) ? nameof(ApiAppPermission.Sansite) : x).ToList();
                return names.Count == 0 ? ApiAppPermission.None : Enum.Parse<ApiAppPermission>(string.Join(", ", names), true);
            }
            return base.ReadJson(reader, objectType, existingValue, serializer);
        }
    }

    public class ApiApp : SanakanApiKey
    {
        [JsonConverter(typeof(ApiAppPermissionConverter))]
        public ApiAppPermission Permissions { get; set; } = ApiAppPermission.UserKeys;

        public bool Has(ApiAppPermission permission) => (Permissions & permission) == permission;
    }
}
