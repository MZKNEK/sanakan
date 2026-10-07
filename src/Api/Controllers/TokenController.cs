#pragma warning disable 1591

using Microsoft.AspNetCore.Mvc;
using System.Linq;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System;
using System.Text;
using System.Security.Claims;
using System.Security.Cryptography;
using Sanakan.Config;
using Sanakan.Extensions;
using Sanakan.Api.Models;
using Sanakan.Services.Time;

namespace Sanakan.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TokenController : ControllerBase
    {
        private readonly ISystemTime _time;
        private readonly IConfig _config;
        private readonly ITokenAttemptGuard _guard;

        public TokenController(IConfig config, ISystemTime time, ITokenAttemptGuard guard)
        {
            _config = config;
            _time = time;
            _guard = guard;
        }

        /// <summary>
        /// Zwraca token ważny jeden dzień
        /// </summary>
        /// <param name="apikey">Key aplikacji</param>
        /// <response code="401">API Key Not Provided</response>
        /// <response code="403">API Key Is Invalid</response>
        [HttpPost, AllowAnonymous]
        public IActionResult CreateToken([FromBody]string apikey)
        {
            if (apikey == null) return "API Key Not Provided".ToResponse(401);

            var clientKey = GetClientKey();
            if (_guard.IsLocked(clientKey, out var retryAfter))
            {
                var hours = (int)retryAfter.TotalHours;
                var minutes = retryAfter.Minutes;
                return $"Zbyt wiele nieudanych prób. Spróbuj ponownie za {hours}h {minutes}m.".ToResponse(429);
            }

            var user = Authenticate(apikey);
            if (user == null)
            {
                _guard.RegisterFailure(clientKey);
                return "API Key Is Invalid".ToResponse(403);
            }

            _guard.RegisterSuccess(clientKey);

            var tokenData = BuildToken(user);
            return Ok(new { token = tokenData.Token, expire = tokenData.Expire });
        }

        private string GetClientKey()
        {
            var ip = HttpContext?.Connection?.RemoteIpAddress;
            return ip?.ToString() ?? "unknown";
        }

        private TokenData BuildToken(string user)
        {
            var config = _config.Get();

            var claims = new[] {
                new Claim(JwtRegisteredClaimNames.Website, user),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            };

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config.Jwt.Key));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(config.Jwt.Issuer,
              config.Jwt.Issuer,
              claims,
              expires: _time.Now().AddHours(24),
              signingCredentials: creds);

            return new TokenData()
            {
                Token = new JwtSecurityTokenHandler().WriteToken(token),
                Expire = token.ValidTo
            };
        }

        private string Authenticate(string apikey)
        {
            if (string.IsNullOrEmpty(apikey))
                return null;

            var keys = _config.Get().ApiKeys;
            if (keys == null)
                return null;

            var provided = Encoding.UTF8.GetBytes(apikey);
            foreach (var key in keys)
            {
                if (key?.Key == null)
                    continue;

                if (CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(key.Key), provided))
                    return key.Bearer;
            }

            return null;
        }
    }
}
