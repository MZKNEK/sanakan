#pragma warning disable 1591

using System;
using System.Collections.Generic;

namespace Sanakan.Services.PocketWaifu
{
    public class ListIdUpdate
    {
        public ListIdUpdate()
        {
            EventEnabled = false;
            Ids = new List<ulong>();
            EventIds = new List<ulong>();
            LastUpdate = DateTime.MinValue;
        }

        public List<ulong> GetIds()
        {
            if (EventEnabled && EventIds.Count > 0)
                return EventIds;

            return Ids;
        }

        public void SetEventIds(List<ulong> ids)
            => EventIds = ids;

        public void Update(List<ulong> ids, DateTime updateTime)
        {
            LastUpdate = updateTime;
            Ids = ids;
        }

        public bool IsNeedForUpdate(DateTime currentTime)
            => (currentTime - LastUpdate).TotalDays >= 1;

        public bool EventEnabled { get; set; }
        public List<ulong> EventIds { get; private set; }

        public List<ulong> Ids { get; private set; }
        public DateTime LastUpdate { get; private set; }
    }
}