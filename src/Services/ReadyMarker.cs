using System;
using System.IO;

namespace Sanakan.Services
{
    // Run.sh names a file in SANAKAN_READY_FILE and goes back to the previous build only when a new one
    // exits before writing it, that is before it connected to Discord and started the API
    public static class ReadyMarker
    {
        public const string Variable = "SANAKAN_READY_FILE";

        public static void Write(string path, ILogger logger = null)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;

            try
            {
                File.WriteAllText(path, DateTime.Now.ToString("O"));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger?.LogError($"Bot: ready marker {path}: {ex.Message}");
            }
        }
    }
}
