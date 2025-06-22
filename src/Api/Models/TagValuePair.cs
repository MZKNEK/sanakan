using System.Collections.Generic;

namespace Sanakan.Api.Models
{
    /// <summary>
    /// Para nazwa oznaczenia z jego id
    /// </summary>
    public class TagIdPair
    {
        /// <summary>
        /// Nazwa oznaczenia
        /// </summary>
        public string Name { get; set; }
        /// <summary>
        /// Id oznaczenia
        /// </summary>
        public ulong Id { get; set; }
    }
}