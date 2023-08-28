#pragma warning disable 1591

using System;
using System.Collections.Generic;
using System.Linq;
using Sanakan.Extensions;
using Sanakan.Services.Time;

namespace Sanakan.Services.Supervisor
{
    public class SupervisorMessage
    {
        private readonly ISystemTime _timeProvider;

        private static readonly List<string> _bannableStrings = new List<string>()
        {
            "dliscord.com", ".gift", "discorl.com", "dliscord-giveaway.ru", "dlscordniltro.com", "dlscocrd.club",
            "dliscordl.com", "boostnltro.com", "discord-gifte", "dlscordapps.com"
        };

        private static readonly List<string> _whitelistUrls = new List<string>()
        {
            "tenor.com", "imgur.com", "sanakan.pl", "shinden.pl"
        };

        public SupervisorMessage(string content, ISystemTime timeProvider, int count = 1)
        {
            _timeProvider = timeProvider;

            PreviousOccurrence = _timeProvider.Now();
            Content = content;
            Count = count;
        }

        public DateTime PreviousOccurrence { get; private set; }
        public string Content { get; private set; }
        public int Count { get; private set; }

        public bool IsBannable() => _bannableStrings.Any(x => Content.Contains(x));
        public bool IsValid() => (_timeProvider.Now() - PreviousOccurrence).TotalMinutes <= 1;
        public int Inc()
        {
            if ((_timeProvider.Now() - PreviousOccurrence).TotalMinutes > 10)
                Count = 0;

            PreviousOccurrence = _timeProvider.Now();

            return ++Count;
        }

        public bool AnyUrl(bool countUrls = false)
        {
            bool found = false;
            foreach (var url in Content.GetURLs())
            {
                if (!_whitelistUrls.Any(x => url.Contains(x)))
                {
                    found = true;
                    if (countUrls)
                        Count++;
                }
            }
            return found;
        }
    }
}
