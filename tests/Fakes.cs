using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Sanakan.Config;
using Sanakan.Config.Model;
using Sanakan.Services.Time;

namespace Artifacts
{
    public class ListLogger : Shinden.Logger.ILogger
    {
        public ConcurrentQueue<string> Messages { get; } = new ConcurrentQueue<string>();
        public void Log(string message) => Messages.Enqueue(message);
    }

    public class FakeConfig : IConfig
    {
        public ConfigModel Model { get; set; } = new ConfigModel
        {
            Jwt = new JwtConfig { Key = "local-test-signing-key-0123456789abcdef", Issuer = "local-test" },
            ApiKeys = new List<SanakanApiKey>(),
            UserKeyApps = new List<ApiApp>(),
        };

        public ConfigModel Get() => Model;
        public void Save() { }
    }

    public class FixedTime : ISystemTime
    {
        public DateTime Value { get; set; } = new DateTime(2026, 1, 1, 12, 0, 0);
        public DateTime Now() => Value;
    }

    public class EmptyServiceProvider : IServiceProvider
    {
        public object GetService(Type serviceType) => null;
    }
}
