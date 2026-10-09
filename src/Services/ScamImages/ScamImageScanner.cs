#pragma warning disable 1591

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Shinden.Logger;

namespace Sanakan.Services.ScamImages
{
    public sealed class ScamImageMatch
    {
        public ScamImageMatch(ulong hash, ulong knownHash, int distance, string url)
        {
            Hash = hash;
            KnownHash = knownHash;
            Distance = distance;
            Url = url;
        }

        public ulong Hash { get; }
        public ulong KnownHash { get; }
        public int Distance { get; }
        public string Url { get; }
    }

    // Downloads message attachments and matches them against the known scam signatures.
    public sealed class ScamImageScanner
    {
        public const int DefaultMaxDistance = 6;

        private readonly ImageProcessing _images;
        private readonly ScamImageStore _store;
        private readonly ILogger _logger;

        public ScamImageScanner(ImageProcessing images, ScamImageStore store, ILogger logger, int maxDistance = DefaultMaxDistance)
        {
            _images = images;
            _store = store;
            _logger = logger;
            MaxDistance = maxDistance;
        }

        public int MaxDistance { get; }
        public int SignatureCount => _store.Count;
        public ScamImageStore Store => _store;

        public async Task<ScamImageMatch> FindMatchAsync(IEnumerable<IAttachment> attachments)
        {
            if (_store.Count == 0 || attachments == null)
                return null;

            foreach (var attachment in attachments)
            {
                var url = attachment?.Url;
                if (string.IsNullOrEmpty(url))
                    continue;

                try
                {
                    using var stream = await _images.TryGetImageStreamAsync(url);
                    if (stream == null || ReferenceEquals(stream, Stream.Null))
                        continue;

                    var match = MatchStream(stream, url);
                    if (match != null)
                        return match;
                }
                catch (Exception ex)
                {
                    _logger?.LogError($"ScamImageScanner: {ex}");
                }
            }

            return null;
        }

        public bool IsScamStream(Stream stream) => MatchStream(stream, null) != null;

        public ScamImageMatch MatchStream(Stream stream, string url)
        {
            if (_store.Count == 0 || stream == null)
                return null;

            if (stream.CanSeek)
                stream.Position = 0;

            foreach (var hash in PerceptualHash.FromStreamOrientations(stream))
                if (_store.TryFind(hash, MaxDistance, out var known, out var distance))
                    return new ScamImageMatch(hash, known, distance, url);

            return null;
        }

        public async Task<int> AddFromAttachmentsAsync(IEnumerable<IAttachment> attachments)
        {
            if (attachments == null)
                return 0;

            int added = 0;
            foreach (var attachment in attachments.Where(x => x != null && !string.IsNullOrEmpty(x.Url)))
            {
                try
                {
                    using var stream = await _images.TryGetImageStreamAsync(attachment.Url);
                    if (stream == null || ReferenceEquals(stream, Stream.Null))
                        continue;

                    if (_store.Add(PerceptualHash.FromStream(stream)))
                        ++added;
                }
                catch (Exception ex)
                {
                    _logger?.LogError($"ScamImageScanner: {ex}");
                }
            }

            return added;
        }

        public bool Remove(ulong hash) => _store.Remove(hash);
    }
}
