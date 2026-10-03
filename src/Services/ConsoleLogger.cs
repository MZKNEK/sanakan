#pragma warning disable 1591

using Sanakan.Config;
using Shinden.Logger;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Sanakan.Services
{
    public class ConsoleLogger : ILogger
    {
        private const string Mask = "***";
        private const int MinSecretLength = 6;

        private static readonly Regex _secretParams = new Regex(@"(?<name>api_?key|token|password|pwd|secret)(?<sep>""?\s*[=:]\s*""?)(?<value>[^&\s;""',]+)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex _sanakanKeys = new Regex(@"\bsnka?_[A-Za-z0-9_-]{20,}", RegexOptions.Compiled);

        private readonly IConfig _config;

        public ConsoleLogger(IConfig config = null)
        {
            _config = config;
        }

        public void Log(string message)
        {
            Console.WriteLine(MaskSecrets(message));
        }

        public string MaskSecrets(string message)
        {
            if (string.IsNullOrEmpty(message))
                return message;

            foreach (var secret in GetSecrets())
                message = message.Replace(secret, Mask);

            message = _secretParams.Replace(message, x => $"{x.Groups["name"].Value}{x.Groups["sep"].Value}{Mask}");
            return _sanakanKeys.Replace(message, Mask);
        }

        private IEnumerable<string> GetSecrets()
        {
            var config = _config?.Get();
            if (config == null)
                return Enumerable.Empty<string>();

            var secrets = new List<string> { config.BotToken, config.Shinden?.Token, config.Jwt?.Key };
            secrets.AddRange(config.ApiKeys?.Select(x => x.Key) ?? Enumerable.Empty<string>());
            secrets.AddRange(config.UserKeyApps?.Select(x => x.Key) ?? Enumerable.Empty<string>());

            return secrets.Where(x => x?.Length >= MinSecretLength).Distinct().OrderByDescending(x => x.Length);
        }
    }
}
