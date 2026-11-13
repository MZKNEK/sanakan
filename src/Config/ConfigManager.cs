#pragma warning disable 1591

using Sanakan.Config.Model;

namespace Sanakan.Config
{
    public class ConfigManager : IConfig
    {
        private ConfigModel _config;
        private JsonFileReader _reader;

        public ConfigManager(string path, bool isDebug = false)
        {
            // people edit the config by hand, so it is kept readable when the bot saves it
            _reader = new JsonFileReader(path, indented: true);
            _config = isDebug ? LoadOrCreateTemplate() : Load();
        }

        public ConfigModel Get() => _config;

        public void Save() => _reader.Save(_config);

        private ConfigModel Load()  => _reader.Load<ConfigModel>();

        private ConfigModel LoadOrCreateTemplate()
        {
            if (!_reader.Exist())
                _reader.Save(new ConfigModel());
                
            return Load();
        }
    }
}
