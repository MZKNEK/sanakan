#pragma warning disable 1591

using System;
using System.Collections.Generic;
using System.Linq;
using Sanakan.Services.Time;

namespace Sanakan.Services.Supervisor
{
    public class SupervisorJoinEntity
    {
        private readonly ISystemTime _timeProvider;

        public int TotalUsers { get; private set; }
        public List<ulong> IDs { get; private set; }
        public DateTime LastJoinTime { get; private set; }

        public SupervisorJoinEntity(ulong id, ISystemTime timeProvider) : this(timeProvider)
        {
            TotalUsers = 1;
            IDs.Add(id);
        }

        public SupervisorJoinEntity(ISystemTime timeProvider)
        {
            _timeProvider = timeProvider;

            IDs = new List<ulong>();
            LastJoinTime = _timeProvider.Now();
            TotalUsers = 0;
        }

        public bool IsBannable() => IsValid() && TotalUsers > 3;
        public bool IsValid() => (_timeProvider.Now() - LastJoinTime).TotalMinutes <= 2;
        public void Add(ulong id)
        {
            if (!IDs.Any(x => x == id))
            {
                IDs.Add(id);
                ++TotalUsers;
            }
        }

        public List<ulong> GetUsersToBan()
        {
            var copy = IDs.ToList();
            IDs.Clear();
            return copy;
        }
    }
}
