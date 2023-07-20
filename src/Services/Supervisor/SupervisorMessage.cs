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

        private static List<string> BannableStrings = new List<string>()
        {
            "dliscord.com", ".gift", "discorl.com", "dliscord-giveaway.ru", "dlscordniltro.com", "dlscocrd.club",
            "dliscordl.com", "boostnltro.com", "discord-gifte", "dlscordapps.com"
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

        public bool ContainsUrl() => Content.GetURLs().Count > 0;
        public bool IsBannable() => BannableStrings.Any(x => Content.Contains(x));
        public bool IsValid() => (_timeProvider.Now() - PreviousOccurrence).TotalMinutes <= 1;
        public int Inc()
        {
            if ((_timeProvider.Now() - PreviousOccurrence).TotalMinutes > 10)
                Count = 0;

            PreviousOccurrence = _timeProvider.Now();

            return ++Count;
        }
    }
}
