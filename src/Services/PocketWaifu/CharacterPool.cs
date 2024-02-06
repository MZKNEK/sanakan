#pragma warning disable 1591

using System;
using System.Collections.Generic;

namespace Sanakan.Services.PocketWaifu
{
    public class CharacterPool<T>
    {
        public CharacterPool()
        {
            Pool = new List<T>();
            LastUpdate = DateTime.MinValue;
        }

        public List<T> GetAll()
        {
            return Pool;
        }

        public T GetOneRandom()
        {
            return Fun.GetOneRandomFrom(Pool);
        }

        public void Update(List<T> ids, DateTime updateTime)
        {
            LastUpdate = updateTime;
            Pool = ids;
        }

        public bool IsNeedForUpdate(DateTime currentTime)
            => (currentTime - LastUpdate).TotalDays >= 1;

        public List<T> Pool { get; private set; }
        public DateTime LastUpdate { get; private set; }
    }
}