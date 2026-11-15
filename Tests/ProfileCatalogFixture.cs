using System;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;
using Sanakan.Api.Controllers;
using Xunit;

namespace Artifacts
{
    // Writes api/Profile/catalog as the bot answers it, for the made-up bot API of the site's local preview.
    // Runs only when SANAKAN_PROFILE_CATALOG_OUT names the file to write.
    public class ProfileCatalogFixture
    {
        [Fact]
        public void WritesTheCatalog()
        {
            var output = Environment.GetEnvironmentVariable("SANAKAN_PROFILE_CATALOG_OUT");
            if (string.IsNullOrEmpty(output))
                return;

            var controller = new ProfileController(null, new ListLogger(), null, new FakeConfig(), new FixedTime(), null);
            var settings = new JsonSerializerSettings
            {
                ContractResolver = new CamelCasePropertyNamesContractResolver(),
                Converters = { new StringEnumConverter(new CamelCaseNamingStrategy()) },
                Formatting = Formatting.Indented,
            };
            File.WriteAllText(output, JsonConvert.SerializeObject(controller.GetCatalog().Value, settings) + "\n");
        }
    }
}
