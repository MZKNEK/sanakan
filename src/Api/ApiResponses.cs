#pragma warning disable 1591

using System;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Sanakan.Extensions;

namespace Sanakan.Api
{
    // Responses shared by the controllers: every 503 and 429 tells the client when to try again.
    public static class ApiResponses
    {
        public const int QueueFullRetryAfter = 10;
        public const int ShindenRetryAfter = 30;
        public const string RetryAfterHeader = "Retry-After";

        public static ObjectResult Unavailable(HttpResponse response, string message, int retryAfterSeconds)
        {
            SetRetryAfter(response, retryAfterSeconds);
            return message.ToResponse(503);
        }

        public static ObjectResult QueueFull(HttpResponse response)
            => Unavailable(response, "Command queue is full", QueueFullRetryAfter);

        public static ObjectResult ShindenUnavailable(HttpResponse response)
            => Unavailable(response, "Can't connect to shinden!", ShindenRetryAfter);

        public static ObjectResult TooManyAttempts(HttpResponse response, TimeSpan retryAfter, string message = "Too many failed attempts")
        {
            SetRetryAfter(response, (int)Math.Ceiling(Math.Max(1, retryAfter.TotalSeconds)));
            return message.ToResponse(429);
        }

        public static ObjectResult KeyRequired() => $"{Services.Idempotency.KeyHeader} is required".ToResponse(400);

        // a conflict the client can tell apart by its code, not only by the message
        public static ObjectResult Conflict(string code, string message, object details = null)
            => new ObjectResult(new { message, success = false, code, details }) { StatusCode = 409 };

        private static void SetRetryAfter(HttpResponse response, int seconds)
        {
            if (response != null)
                response.Headers[RetryAfterHeader] = seconds.ToString();
        }
    }
}
