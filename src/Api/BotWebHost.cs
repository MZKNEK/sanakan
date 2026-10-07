#pragma warning disable 1591

using Microsoft.AspNetCore.Hosting;
using System.Threading;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.HttpOverrides;
using System.IO;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Extensions.Logging;
using System.Text;
using Sanakan.Config;
using Sanakan.Services.Executor;
using Discord.WebSocket;
using Shinden;
using Sanakan.Services.PocketWaifu;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;
using Microsoft.OpenApi.Models;
using Sanakan.Services.Time;
using Asp.Versioning;
using System;
using System.Threading.Tasks;
using Sanakan.Config.Model;

namespace Sanakan.Api
{
    public static class BotWebHost
    {
        private const int MinJwtKeyBytes = 32;

        private static IWebHost _host;

        public static void RunWebHost(DiscordSocketClient client, ShindenClient shinden, Waifu waifu, IConfig config, Services.Helper helper,
            IExecutor executor, Shinden.Logger.ILogger logger, ISystemTime time, TagHelper tags, Expedition expedition, HealthMonitor health)
        {
            var host = CreateWebHostBuilder(config).ConfigureServices(services =>
            {
                services.AddSingleton(tags);
                services.AddSingleton(time);
                services.AddSingleton(waifu);
                services.AddSingleton(logger);
                services.AddSingleton(client);
                services.AddSingleton(helper);
                services.AddSingleton(shinden);
                services.AddSingleton(executor);
                services.AddSingleton(expedition);
                services.AddSingleton(health);
            }).Build();

            _host = host;
            new Thread(() =>
            {
                try
                {
                    host.Run();
                }
                catch (Exception ex)
                {
                    logger.LogError($"API przestało działać: {ex}");
                    Environment.Exit(1);
                }
            }) { IsBackground = true }.Start();
        }

        public static void ValidateConfig(ConfigModel config)
        {
            if (config.Jwt == null || string.IsNullOrWhiteSpace(config.Jwt.Key) || Encoding.UTF8.GetByteCount(config.Jwt.Key) < MinJwtKeyBytes)
                throw new InvalidOperationException($"Jwt.Key musi być ustawiony i mieć co najmniej {MinJwtKeyBytes} bajty");

            if (string.IsNullOrWhiteSpace(config.Jwt.Issuer))
                throw new InvalidOperationException("Jwt.Issuer musi być ustawiony");
        }

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

        private static IWebHostBuilder CreateWebHostBuilder(IConfig config) =>
            WebHost.CreateDefaultBuilder().ConfigureServices(services => AddApiServices(services, config))
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddSimpleConsole(x => x.ColorBehavior = Microsoft.Extensions.Logging.Console.LoggerColorBehavior.Disabled);
                logging.SetMinimumLevel(LogLevel.Warning);
            })
            .Configure(UseApi)
#if !DEBUG
            ;
#else
            .UseUrls("http://*:5005");
#endif

        public static void AddApiServices(IServiceCollection services, IConfig config)
        {
            var tmpCnf = config.Get();
            services.AddMemoryCache();
            services.AddSingleton(config);
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
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(tmpCnf.Jwt.Key))
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
                    Description = "Autentykacja następuje poprzez dopasowanie tokenu przesłanego w ciele zapytania `api/token`, a następnie wysyłania w nagłówku `Authorization` z przedrostkiem `Bearer` otrzymanego w zwrocie tokena."
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
                await next();
                if (context.Request.Path.StartsWithSegments("/api/health") || context.Request.Path.StartsWithSegments("/api/alive")) return;

                ApiStats.Add(context);
                var entry = ApiAudit.Describe(context, watch.ElapsedMilliseconds);
                if (entry != null) auditLogger?.Log(entry);
                else traffic.Add(context);
            });
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
            });
        }
    }
}
