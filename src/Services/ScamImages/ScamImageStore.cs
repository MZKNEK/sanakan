#pragma warning disable 1591

using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Sanakan.Services.ScamImages
{
    // File-backed set of perceptual hashes of known scam images.
    public sealed class ScamImageStore
    {
        private const string Header = "# pHash (16 hex chars) of known scam images, one per line. Lines starting with # are ignored.";

        private readonly object _lock = new object();
        private readonly HashSet<ulong> _hashes = new HashSet<ulong>();
        private readonly string _path;

        public ScamImageStore(string path)
        {
            _path = path;
            if (!string.IsNullOrEmpty(_path) && !File.Exists(_path))
                Save();

            Reload();
        }

        public string Path => _path;

        public int Count
        {
            get { lock (_lock) return _hashes.Count; }
        }

        public ulong[] Snapshot()
        {
            lock (_lock) return _hashes.ToArray();
        }

        public void Reload()
        {
            lock (_lock)
            {
                _hashes.Clear();
                if (string.IsNullOrEmpty(_path) || !File.Exists(_path))
                    return;

                foreach (var line in File.ReadLines(_path))
                    if (TryParseHash(line, out var hash))
                        _hashes.Add(hash);
            }
        }

        public bool Add(ulong hash)
        {
            lock (_lock)
            {
                if (!_hashes.Add(hash))
                    return false;

                Save();
                return true;
            }
        }

        public bool Remove(ulong hash)
        {
            lock (_lock)
            {
                if (!_hashes.Remove(hash))
                    return false;

                Save();
                return true;
            }
        }

        public bool Contains(ulong hash, int maxDistance) => TryFind(hash, maxDistance, out _, out _);

        public bool TryFind(ulong hash, int maxDistance, out ulong knownHash, out int distance)
        {
            knownHash = 0;
            distance = int.MaxValue;

            lock (_lock)
            {
                bool found = false;
                foreach (var known in _hashes)
                {
                    int d = PerceptualHash.Distance(hash, known);
                    if (d <= maxDistance && d < distance)
                    {
                        distance = d;
                        knownHash = known;
                        found = true;
                    }
                }

                return found;
            }
        }

        public int MinDistance(ulong hash)
        {
            lock (_lock)
            {
                if (_hashes.Count == 0)
                    return -1;

                int min = int.MaxValue;
                foreach (var known in _hashes)
                {
                    int distance = PerceptualHash.Distance(hash, known);
                    if (distance < min)
                        min = distance;
                }

                return min;
            }
        }

        public static string FormatHash(ulong hash) => hash.ToString("x16");

        public static bool TryParseHash(string value, out ulong hash)
        {
            hash = 0;
            if (string.IsNullOrWhiteSpace(value))
                return false;

            var text = value.Trim();
            int comment = text.IndexOf('#');
            if (comment >= 0)
                text = text.Substring(0, comment).Trim();

            if (text.Length != 16)
                return false;

            return ulong.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out hash);
        }

        private void Save()
        {
            var directory = System.IO.Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            var lines = new List<string> { Header };
            lines.AddRange(_hashes.OrderBy(x => x).Select(FormatHash));
            File.WriteAllLines(_path, lines);
        }
    }
}
