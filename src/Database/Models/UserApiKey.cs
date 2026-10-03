#pragma warning disable 1591

using System;

namespace Sanakan.Database.Models
{
    public class UserApiKey
    {
        public ulong Id { get; set; }
        public ulong UserId { get; set; }
        public string Application { get; set; }
        public string KeyHash { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
