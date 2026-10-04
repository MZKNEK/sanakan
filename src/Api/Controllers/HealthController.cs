#pragma warning disable 1591

using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sanakan.Api.Models;

namespace Sanakan.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class HealthController : ControllerBase
    {
        private readonly HealthMonitor _health;

        public HealthController(HealthMonitor health)
        {
            _health = health;
        }

        /// <summary>
        /// Pobiera stan bota (Discord, baza, Shinden, ruch poleceń)
        /// </summary>
        /// <remarks>Odpowiedź jest trzymana w pamięci przez 15 s. Bazy i Shindena nie sprawdzamy, jeśli bot korzystał z nich poprawnie w bieżącej minucie; w przeciwnym razie wynik sprawdzenia jest trzymany przez 20 s.</remarks>
        /// <response code="200">Stan ok lub degraded</response>
        /// <response code="503">Stan down - bot nie jest połączony z Discordem</response>
        [HttpGet, AllowAnonymous]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        [ProducesResponseType(typeof(HealthStatus), 200)]
        [ProducesResponseType(typeof(HealthStatus), 503)]
        public async Task<ActionResult<HealthStatus>> GetHealthAsync()
        {
            var health = await _health.GetAsync();
            return StatusCode(health.Status == HealthMonitor.Down ? 503 : 200, health);
        }
    }
}
