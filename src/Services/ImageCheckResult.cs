#pragma warning disable 1591

using Sanakan.Extensions;

namespace Sanakan.Services
{
    public class ImageCheckResult
    {
        public ImageUrlCheckResult Status { get; private set; }
        public string Url { get; private set; }

        static public ImageCheckResult From(ImageUrlCheckResult type, string newUrl = null) => new ImageCheckResult
        {
            Status = type,
            Url = newUrl,
        };

        public bool IsOk() => !IsError();
        public bool IsError() => Status != ImageUrlCheckResult.Ok && Status != ImageUrlCheckResult.UrlTransformed;

        public static implicit operator bool(ImageCheckResult result) => result?.IsOk() ?? false;
        public static implicit operator ImageUrlCheckResult(ImageCheckResult result) => result?.Status ?? ImageUrlCheckResult.NotUrl;
        public static implicit operator string(ImageCheckResult result) => result?.Url ?? string.Empty;
    }
}
