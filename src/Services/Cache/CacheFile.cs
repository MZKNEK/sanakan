#pragma warning disable 1591

using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Sanakan.Services.Cache
{
    public static class CacheFile
    {
        // written to a temporary file first so a crash mid-write never leaves a broken cache behind
        public static async Task WriteAsync(string file, object value)
        {
            var dir = Path.GetDirectoryName(file);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var tmp = file + ".tmp";
            await File.WriteAllTextAsync(tmp, JsonConvert.SerializeObject(value)).ConfigureAwait(false);
            File.Move(tmp, file, true);
        }
    }
}
