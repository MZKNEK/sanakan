#pragma warning disable 1591

using System;
using System.Threading.Tasks;
using Discord.WebSocket;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Sanakan.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DebugController : ControllerBase
    {
        private readonly ILogger _logger;
        private readonly DiscordSocketClient _client;

        public DebugController(DiscordSocketClient client, ILogger logger)
        {
            _client = client;
            _logger = logger;
        }

        internal static Action<int> Exit = Environment.Exit;
        internal static TimeSpan ExitDelay = TimeSpan.FromMilliseconds(1500);

        /// <summary>
        /// Zabija bota
        /// </summary>
        /// <response code="202">Bot kończy pracę zaraz po wysłaniu tej odpowiedzi</response>
        [HttpPost("kill"), Authorize(Policy = "Site")]
        public Task<IActionResult> RestartBotAsync() => StopAsync("kill", ExitCodes.Stop);

        /// <summary>
        /// Aktualizuje bota
        /// </summary>
        /// <response code="202">Bot kończy pracę zaraz po wysłaniu tej odpowiedzi</response>
        [HttpPost("update"), Authorize(Policy = "Site")]
        public Task<IActionResult> UpdateBotAsync() => StopAsync("update", ExitCodes.Update);

        // The process ends only after the answer went out; a broken connection instead would make the caller retry
        // and stop the bot again after its restart.
        private async Task<IActionResult> StopAsync(string action, int exitCode)
        {
            await _client.LogoutAsync();
            _logger.Log($"API: {action} requested from web");
            Response.OnCompleted(() =>
            {
                _ = Task.Run(async () =>
                {
                    await Task.Delay(ExitDelay);
                    Exit(exitCode);
                });
                return Task.CompletedTask;
            });
            return Accepted();
        }
    }
}