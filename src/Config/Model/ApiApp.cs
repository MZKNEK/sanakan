#pragma warning disable 1591

using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Sanakan.Config.Model
{
    [Flags]
    public enum ApiAppPermission
    {
        None = 0,
        UserKeys = 1,
        Info = 2,
        Site = 4,
    }

    public class ApiApp : SanakanApiKey
    {
        [JsonConverter(typeof(StringEnumConverter))]
        public ApiAppPermission Permissions { get; set; } = ApiAppPermission.UserKeys;

        public bool Has(ApiAppPermission permission) => (Permissions & permission) == permission;
    }
}
