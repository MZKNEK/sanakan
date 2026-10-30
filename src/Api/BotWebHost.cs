#pragma warning disable 1591

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using System.Threading;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.HttpOverrides;
using System.IO;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Security.Cryptography;
using Sanakan.Config;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;
using Microsoft.OpenApi.Models;
using Asp.Versioning;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Sanakan.Config.Model;

namespace Sanakan.Api
{
    public static class BotWebHost
    {
        private const int MinJwtKeyBytes = 32;
        public const int MinAppKeyLength = 32;

#if DEBUG
        public const string Url = "http://*:5005";
#else
        public const string Url = "http://localhost:5000";
#endif

        private static IHost _host;

        public static async Task StartAsync(IConfig config, IServiceCollection botServices, Shinden.Logger.ILogger logger)
        {
            var host = CreateHost(config, botServices, Url);
            try
            {
                await host.StartAsync();
            }
            catch (Exception ex)
            {
                logger.LogError($"API nie wystartowało: {ex}");
                Environment.Exit(1);
            }

            _host = host;
        }

        public static IHost CreateHost(IConfig config, IServiceCollection botServices, string url) =>
            Host.CreateDefaultBuilder()
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddSimpleConsole(x => x.ColorBehavior = Microsoft.Extensions.Logging.Console.LoggerColorBehavior.Disabled);
                logging.SetMinimumLevel(LogLevel.Warning);
            })
            .ConfigureWebHostDefaults(web => web
                .ConfigureServices(services =>
                {
                    AddApiServices(services, config);
                    AddBotInstances(services, botServices);
                    // the bot handles Ctrl+C and stops the API itself
                    services.AddSingleton<IHostLifetime, BotLifetime>();
                })
                .Configure(UseApi)
                .UseUrls(url))
            .Build();

        // only ready instances are shared; type registrations would create a second copy in the API container
        public static void AddBotInstances(IServiceCollection services, IServiceCollection botServices)
        {
            foreach (var service in botServices.Where(x => x.ImplementationInstance != null))
                services.Add(service);
        }

        private sealed class BotLifetime : IHostLifetime
        {
            public Task WaitForStartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
            public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        }

        public static void ValidateConfig(ConfigModel config)
        {
            if (config.Jwt == null || string.IsNullOrWhiteSpace(config.Jwt.Key) || Encoding.UTF8.GetByteCount(config.Jwt.Key) < MinJwtKeyBytes)
                throw new InvalidOperationException($"Jwt.Key musi być ustawiony i mieć co najmniej {MinJwtKeyBytes} bajty");

            if (string.IsNullOrWhiteSpace(config.Jwt.Issuer))
                throw new InvalidOperationException("Jwt.Issuer musi być ustawiony");
        }

        public static List<string> WeakAppKeys(ConfigModel config)
            => config.UserKeyApps?.Where(x => !string.IsNullOrEmpty(x?.Key) && x.Key.Length < MinAppKeyLength).Select(x => x.Bearer).ToList()
                ?? new List<string>();

        public static SymmetricSecurityKey SigningKey(IConfig config)
            => new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config.Get().Jwt.Key));

        public static string GenerateJwtKey() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));

        public static async Task StopAsync(TimeSpan timeout)
        {
            if (_host == null) return;

            try
            {
                using var cts = new CancellationTokenSource(timeout);
                await _host.StopAsync(cts.Token);
            }
            catch (OperationCanceledException) { }
        }

        public static void AddApiServices(IServiceCollection services, IConfig config)
        {
            var tmpCnf = config.Get();
            services.AddMemoryCache();
            services.AddSingleton(config);
            services.AddSingleton<ITokenAttemptGuard, TokenAttemptGuard>();
            services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(opt =>
            {
                opt.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = tmpCnf.Jwt.Issuer,
                    ValidAudience = tmpCnf.Jwt.Issuer,
                    // read on every request so the key can be rotated without a restart
                    IssuerSigningKeyResolver = (token, securityToken, kid, parameters) => new[] { SigningKey(config) }
                };
            }).AddScheme<AuthenticationSchemeOptions, UserKeyAuthenticationHandler>(UserKeyAuthenticationHandler.SchemeName, null)
              .AddScheme<AuthenticationSchemeOptions, AppKeyAuthenticationHandler>(AppKeyAuthenticationHandler.SchemeName, null);
            services.AddAuthorization(op =>
            {
                // deny-by-default: endpointy bez jawnej polityki/anonymous wymagają zalogowania
                op.FallbackPolicy = new AuthorizationPolicyBuilder()
                    .RequireAuthenticatedUser()
                    .Build();

                op.AddPolicy("Player", policy =>
                {
                    policy.AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme, UserKeyAuthenticationHandler.SchemeName);
                    policy.RequireAuthenticatedUser();

                    policy.RequireAssertion(context => context.User.HasClaim(c => c.Type == "Player" && c.Value == "waifu_player"));
                });

                op.AddPolicy("Site", policy =>
                {
                    policy.AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme, AppKeyAuthenticationHandler.SchemeName);
                    policy.RequireAuthenticatedUser();

                    policy.RequireAssertion(context => AppKeyAuthenticationHandler.IsAllowed(context.User, ApiAppPermission.Site));
                });

                op.AddPolicy("Info", policy =>
                {
                    policy.AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme, AppKeyAuthenticationHandler.SchemeName);
                    policy.RequireAuthenticatedUser();

                    policy.RequireAssertion(context => AppKeyAuthenticationHandler.IsAllowed(context.User, ApiAppPermission.Info));
                });
            });
            services.AddControllers()
                .AddNewtonsoftJson(o => o.SerializerSettings.ReferenceLoopHandling = Newtonsoft.Json.ReferenceLoopHandling.Ignore)
                .AddNewtonsoftJson(o => o.SerializerSettings.Converters.Add(new StringEnumConverter { NamingStrategy = new CamelCaseNamingStrategy() }));
            services.AddCors(options =>
            {
                options.AddPolicy("AllowEverything", builder =>
                {
                    builder.AllowAnyOrigin();
                    builder.AllowAnyHeader();
                    builder.AllowAnyMethod();
                });
            });
            services.AddApiVersioning(o =>
            {
                o.AssumeDefaultVersionWhenUnspecified = true;
                o.DefaultApiVersion = new ApiVersion(1, 0);
                o.ApiVersionReader = new HeaderApiVersionReader("x-api-version");
            });
            services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v2", new OpenApiInfo
                {
                    Title = "Sanakan API",
                    Version = "1.0",
                    Description = "Autentykacja następuje poprzez dopasowanie klucza aplikacji z uprawnieniem `Site` przesłanego w ciele zapytania `api/token`, a następnie wysyłania w nagłówku `Authorization` z przedrostkiem `Bearer` otrzymanego w zwrocie tokena."
                        + "\n\nEndpointy wymagające użytkownika (`Player`) akceptują również klucz użytkownika przesłany w nagłówku `x-user-key`. Klucze generuje aplikacja z uprawnieniem `UserKeys` przez `api/userkey`, podając swój klucz w nagłówku `x-app-key`."
                        + "\n\nEndpointy z polityką `Info` (polecenia moderatorskie, uprawnienia użytkowników) akceptują poza tokenem strony również klucz aplikacji z uprawnieniem `Info` przesłany w nagłówku `x-app-key`. Klucz aplikacji z uprawnieniem `Site` daje dostęp do wszystkich endpointów strony (`Site` i `Info`)."
                        + "\n\nDocelowa wersja api powinna zostać przesłana pod nagłówkiem `x-api-version`, w przypadku jej niepodania zapytania są interpretowane jako wysłane do wersji `1.0`.",
                });

                var filePath = Path.Combine(System.AppContext.BaseDirectory, "Sanakan.xml");
                if (File.Exists(filePath)) c.IncludeXmlComments(filePath);

                c.CustomSchemaIds(x => x.FullName);
            });
        }

        public static void UseApi(IApplicationBuilder app)
        {
            app.UseSwagger();
            app.UseCors("AllowEverything");
            app.UseForwardedHeaders(new ForwardedHeadersOptions { ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto });
            app.UseStaticFiles();
            app.UseRouting();
            app.UseAuthentication();
            app.UseAuthorization();

            var auditLogger = app.ApplicationServices.GetService<Shinden.Logger.ILogger>();
            var traffic = new ApiTraffic(x => auditLogger?.Log(x), System.TimeSpan.FromHours(1));
            app.Use(async (context, next) =>
            {
                var watch = System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    await next();
                }
                catch (Exception ex)
                {
                    auditLogger?.LogError($"API: {context.Request.Method} {context.Request.Path} - {ex}");
                    if (context.Response.HasStarted) throw;

                    context.Response.Clear();
                    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                    await context.Response.WriteAsJsonAsync(new { message = "Internal server error", success = false });
                }
                finally
                {
                    if (!context.Request.Path.StartsWithSegments("/api/health") && !context.Request.Path.StartsWithSegments("/api/alive"))
                    {
                        ApiStats.Add(context);
                        var entry = ApiAudit.Describe(context, watch.ElapsedMilliseconds);
                        if (entry != null) auditLogger?.Log(entry);
                        else traffic.Add(context);
                    }
                }
            });
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
            });
        }
    }
}
