using System;
using System.IO;
using Sanakan.Config;
using Sanakan.Config.Model;
using Xunit;

namespace Artifacts
{
    public class ConfigFileTests : IDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), "sanakan-config-" + Guid.NewGuid().ToString("N") + ".json");

        public void Dispose()
        {
            if (File.Exists(_path))
                File.Delete(_path);
        }

        [Fact]
        public void SavedConfigIsIndentedAndLoadsBack()
        {
            File.WriteAllText(_path, "{\"Prefix\":\"s.\",\"FetchProxy\":{\"HttpProxy\":\"http://proxy.test:1/\"}}");
            var config = new ConfigManager(_path);

            config.Save();

            var text = File.ReadAllText(_path);
            Assert.Contains("\n  \"Prefix\": \"s.\"", text.Replace("\r\n", "\n"));
            Assert.Contains("\n    \"HttpProxy\": \"http://proxy.test:1/\"", text.Replace("\r\n", "\n"));
            var loaded = new ConfigManager(_path).Get();
            Assert.Equal("s.", loaded.Prefix);
            Assert.Equal("http://proxy.test:1/", loaded.FetchProxy.HttpProxy);
        }

        [Fact]
        public void OtherFilesStayOnOneLine()
        {
            new JsonFileReader(_path).Save(new { A = 1, B = new[] { 2 } });

            Assert.Equal("{\"A\":1,\"B\":[2]}", File.ReadAllText(_path));
        }
    }
}
