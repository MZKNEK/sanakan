#pragma warning disable 1591

using System;

namespace Sanakan.Database.Models
{
    public class IdempotencyRecord
    {
        public ulong Id { get; set; }
        public string Scope { get; set; }
        public string Key { get; set; }
        public ulong OwnerId { get; set; }
        public string RequestHash { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
