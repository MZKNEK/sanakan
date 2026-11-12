#pragma warning disable 1591

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Sanakan.Config;
using Sanakan.Extensions;

namespace Sanakan.Services
{
    public class Helper
    {
        private IConfig _config;
        private HttpClient _httpClient;
        private ILogger _log;

        public IEnumerable<ModuleInfo> PublicModulesInfo { get; set; }
        public Dictionary<string, ModuleInfo> PrivateModulesInfo { get; set; }

        public Helper(IConfig config, ILogger log, HttpMessageHandler handler = null)
        {
            _log = log;
            _config = config;
            _httpClient = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false });

            PublicModulesInfo = new List<ModuleInfo>();
            PrivateModulesInfo = new Dictionary<string, ModuleInfo>();
        }

        public async Task<HttpStatusCode> GetResponseFromUrl(string url)
        {
            using var res = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            return res.StatusCode;
        }

        public string GivePublicHelp(string guildPrefix = null)
        {
            var botPrefix = guildPrefix ?? _config.Get().Prefix;

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("**Lista poleceń:**");

            foreach (var item in GetInfoAboutModules(PublicModulesInfo))
            {
                sb.Append($"\n**{item.Name}:**");

                foreach (var mod in item.Modules)
                {
                    string prefix = mod.Prefix;
                    string commands = string.Join("  ", mod.Commands);

                    if (!string.IsNullOrWhiteSpace(prefix))
                    {
                        sb.Append($" ***{prefix}*** {commands}");
                    }
                    else
                    {
                        sb.Append($" {commands}");
                    }
                }
            }

            sb.AppendLine($"\n\nUżyj `{botPrefix}pomoc [polecenie]`, aby uzyskać informacje dotyczące danego polecenia.");

            return sb.ToString();
        }

        public string GivePrivateHelp(string moduleName)
        {
            var moduleInfo = GetInfoAboutModule(PrivateModulesInfo[moduleName]);
            return $"**Lista poleceń:**\n\n**{moduleInfo.Prefix}:** " + string.Join("  ", moduleInfo.Commands);
        }

        public Embed GiveHelpAboutPrivateCmd(string moduleName, string command, string prefix, bool throwEx = true)
        {
            if (!PrivateModulesInfo.TryGetValue(moduleName, out var info))
            {
                if (throwEx)
                    throw new Exception($"Nie znaleziono modułu o nazwie {moduleName}.");

                return null;
            }

            var thisCommands = info.Commands.FirstOrDefault(x => x.Name == command)
                               ?? info.Commands.FirstOrDefault(x => x.Aliases.Contains(command));

            if (thisCommands != null)
                return GetCommandInfo(thisCommands, prefix);

            if (throwEx)
                throw new Exception($"Nie znaleziono polecenia {command} w module {moduleName}.");

            return null;
        }

        public Embed GetCommandInfo(CommandInfo cmd, string prefix = null)
        {
            var name = $"{prefix ?? _config.Get().Prefix}{GetModGroupPrefix(cmd.Module)}{cmd.Name}";

            var embed = new EmbedBuilder
            {
                Title = $"ℹ️ {name}".TrimToLength(EmbedBuilder.MaxTitleLength),
                Color = Commands.CommandErrors.HelpColor,
                Description = cmd.Summary,
            };

            var arguments = Commands.CommandErrors.FormatArguments(cmd.Parameters.Where(x => !x.IsHidden())
                .Select(x => (string.IsNullOrWhiteSpace(x.Summary) ? x.Name : x.Summary, x.IsOptional)));
            if (arguments.Length > 0)
                embed.AddField("Argumenty", arguments.TrimToLength(EmbedFieldBuilder.MaxFieldValueLength));

            var example = string.IsNullOrWhiteSpace(cmd.Remarks) ? name : $"{name} {cmd.Remarks}";
            embed.AddField("Przykład", $"`{example}`".TrimToLength(EmbedFieldBuilder.MaxFieldValueLength));

            var aliases = cmd.Aliases.Where(x => x != cmd.Name).Select(x => $"`{x}`").ToList();
            if (aliases.Count > 0)
                embed.AddField("Aliasy", string.Join(" ", aliases).TrimToLength(EmbedFieldBuilder.MaxFieldValueLength));

            return embed.Build();
        }

        public Embed GiveHelpAboutPublicCmd(string command, string prefix, bool admin = false, bool dev = false)
        {
            var matchingCommands = PublicModulesInfo
                .SelectMany(module => module.Commands)
                .Where(cmd => cmd.Name == command || cmd.Aliases.Any(alias => alias == command))
                .ToList();

            if (matchingCommands.Any())
            {
                return GetCommandInfo(matchingCommands.First(), prefix);
            }

            if (admin)
            {
                var res = GiveHelpAboutPrivateCmd("Moderacja", command, prefix, false);
                if (res != null) return res;
            }

            if (dev)
            {
                var res = GiveHelpAboutPrivateCmd("Debug", command, prefix, false);
                if (res != null) return res;
            }

            throw new Exception("Polecenie nie istnieje!");
        }

        private List<SanakanModuleInfo> GetInfoAboutModules(IEnumerable<ModuleInfo> modules)
        {
            var moduleInfos = new List<SanakanModuleInfo>();

            foreach (var module in modules)
            {
                var moduleInfo = moduleInfos.FirstOrDefault(x => x.Name == module.Name);

                if (moduleInfo == null)
                {
                    moduleInfo = new SanakanModuleInfo
                    {
                        Name = module.Name,
                        Modules = new List<SanakanSubModuleInfo>()
                    };

                    moduleInfos.Add(moduleInfo);
                }

                var subModuleInfo = new SanakanSubModuleInfo
                {
                    Prefix = GetModGroupPrefix(module, false),
                    Commands = module.Commands
                        .Where(x => !string.IsNullOrEmpty(x.Name) && !x.IsHidden())
                        .OrderBy(x => x.Name)
                        .Select(x => $"`{x.Name}`")
                        .ToList()
                };

                if (!moduleInfo.Modules.Any(x => x.Prefix == subModuleInfo.Prefix))
                {
                    moduleInfo.Modules.Add(subModuleInfo);
                }
            }

            return moduleInfos;
        }

        private SanakanSubModuleInfo GetInfoAboutModule(ModuleInfo module)
            => new SanakanSubModuleInfo
            {
                Prefix = module.Name,
                Commands = module.Commands
                    .Where(cmd => !string.IsNullOrEmpty(cmd.Name) && !cmd.IsHidden())
                    .OrderBy(x => x.Name)
                    .Select(cmd => $"`{cmd.Name}`")
                    .Distinct()
                    .ToList()
            };

        private string GetModGroupPrefix(ModuleInfo mod, bool space = true)
        {
            var alias = mod.Aliases.FirstOrDefault();
            if (!string.IsNullOrEmpty(alias))
            {
                if (space) alias += " ";
                return alias;
            }
            return "";
        }

        private class SanakanModuleInfo
        {
            public string Name { get; set; }
            public List<SanakanSubModuleInfo> Modules { get; set; }
        }

        private class SanakanSubModuleInfo
        {
            public string Prefix { get; set; }
            public List<string> Commands { get; set; }
        }

        public Embed GetInfoAboutUser(SocketGuildUser user)
        {
            return new EmbedBuilder
            {
                Author = new EmbedAuthorBuilder().WithUser(user),
                ThumbnailUrl = user.GetUserOrDefaultAvatarUrl(),
                Fields = GetInfoUserFields(user),
                Color = EMType.Info.Color(),
            }.Build();
        }

        private List<EmbedFieldBuilder> GetInfoUserFields(SocketGuildUser user)
        {
            string roles = "Brak";
            if (user.Roles.Count > 1)
            {
                // a field value holds at most 1024 characters, a longer one makes Discord reject the whole message
                roles = Moderator.JoinWithinLimit(user.Roles
                    .OrderByDescending(x => x.Position)
                    .Where(x => !x.IsEveryone)
                    .Select(x => x.Mention).ToList(), EmbedFieldBuilder.MaxFieldValueLength, " ");
            }

            return new List<EmbedFieldBuilder>
            {
                new EmbedFieldBuilder
                {
                    Name = "Id",
                    Value = user.Id.ToString(),
                    IsInline = true
                },
                new EmbedFieldBuilder
                {
                    Name = "Global",
                    Value = user.GlobalName ?? "Brak",
                    IsInline = true
                },
                new EmbedFieldBuilder
                {
                    Name = "Pseudo",
                    Value = user.Nickname ?? "Brak",
                    IsInline = true
                },
                new EmbedFieldBuilder
                {
                    Name = "Bot",
                    Value = user.IsBot ? "Tak" : "Nie",
                    IsInline = true
                },
                new EmbedFieldBuilder
                {
                    Name = "Utworzono",
                    Value = user.CreatedAt.LocalDateTime.ToShortDateTime(),
                    IsInline = false
                },
                new EmbedFieldBuilder
                {
                    Name = "Dołączono",
                    Value = user.JoinedAt?.LocalDateTime.ToShortDateTime() ?? "Nieznane",
                    IsInline = false
                },
                new EmbedFieldBuilder
                {
                    Name = $"Role[{user.Roles.Count - 1}]",
                    Value = roles,
                    IsInline = false
                }
            };
        }

        public Embed GetInfoAboutServer(SocketGuild guild)
        {
            var author = new EmbedAuthorBuilder().WithName(guild.Name);
            if (guild.IconUrl != null) author.WithIconUrl(guild.IconUrl);

            var embed = new EmbedBuilder
            {
                Fields = GetInfoGuildFields(guild),
                Color = EMType.Info.Color(),
                Author = author,
            };

            if (guild.IconUrl != null) embed.WithThumbnailUrl(guild.IconUrl);

            return embed.Build();
        }

        private List<EmbedFieldBuilder> GetInfoGuildFields(SocketGuild guild)
        {
            var roles = guild.Roles
                .Where(role => !role.IsEveryone && !ulong.TryParse(role.Name, out _))
                .OrderByDescending(role => role.Position)
                .Select(role => role.Mention)
                .ToList();

            return new List<EmbedFieldBuilder>
            {
                new EmbedFieldBuilder
                {
                    Name = "Id",
                    Value = guild.Id.ToString(),
                    IsInline = true
                },
                new EmbedFieldBuilder
                {
                    Name = "Właściciel",
                    Value = guild.Owner.Mention,
                    IsInline = true
                },
                new EmbedFieldBuilder
                {
                    Name = "Utworzono",
                    Value = guild.CreatedAt.LocalDateTime.ToShortDateTime(),
                    IsInline = true
                },
                new EmbedFieldBuilder
                {
                    Name = "Liczba użytkowników",
                    Value = guild.Users.Count.ToString(),
                    IsInline = true
                },
                new EmbedFieldBuilder
                {
                    Name = "Kanały tekstowe",
                    Value = guild.TextChannels.Count.ToString(),
                    IsInline = true
                },
                new EmbedFieldBuilder
                {
                    Name = "Kanały głosowe",
                    Value = guild.VoiceChannels.Count.ToString(),
                    IsInline = true
                },
                new EmbedFieldBuilder
                {
                    Name = $"Role[{guild.Roles.Count}]",
                    Value = string.Join(" ", roles).TrimToLength(EmbedFieldBuilder.MaxFieldValueLength),
                    IsInline = false
                }
            };
        }

        public async Task<IMessage> FindMessageInGuildAsync(SocketGuild guild, ulong id)
        {
            foreach (var channel in guild.TextChannels)
            {
                IMessage msg = await channel.GetMessageAsync(id);
                if (msg != null)
                {
                    return msg;
                }
            }
            return null;
        }

        public void AppendMessage(List<Embed> embeds, MessageContent currentContent, string nextPart)
        {
            if (currentContent.Length > 0 && (currentContent.Length + nextPart.Length > DiscordLimits.SafeDescriptionLength
                || currentContent.Elements + DiscordLimits.CountElements(nextPart) > DiscordLimits.SafeMarkdownElements))
            {
                embeds.Add(new EmbedBuilder() { Color = EMType.Info.Color(), Description = currentContent.ToString() }.Build());
                currentContent.Clear();
            }
            currentContent.Append(nextPart);
        }

        public List<Embed> BuildEmbedsFormTextRows(IEnumerable<string> rows)
        {
            var list = new List<Embed>();
            var contentString = new MessageContent();
            foreach (var row in rows)
            {
                AppendMessage(list, contentString, $"{row}\n");
            }

            list.Add(new EmbedBuilder()
            {
                Color = EMType.Info.Color(),
                Description = contentString.ToString()
            }.Build());

            return list;
        }

        // a page per second stays under the usual 5 messages per 5 s of a channel; Discord.Net also waits when the bucket runs out
        public static readonly TimeSpan DmPageDelay = TimeSpan.FromSeconds(1);

        internal Func<TimeSpan, Task> Wait { get; set; } = Task.Delay;

        public async Task<ExecutionResult> SendEmbedsOnDMAsync(IUser user, IEnumerable<Embed> embeds, bool tldr = false)
            => await SendEmbedsOnDMAsync(user, embeds, DmPageDelay, tldr);

        // the remaining pages of the last list, for tests
        internal Task LastPagesSending { get; private set; } = Task.CompletedTask;

        // the answer comes after the first page, the rest follows in the background at the safe pace;
        // a closed DM shows up already on the first page
        public async Task<ExecutionResult> SendEmbedsOnDMAsync(IUser user, IEnumerable<Embed> embeds, TimeSpan delay, bool tldr)
        {
            var pages = embeds.ToList();
            IDMChannel dm;
            try
            {
                dm = await user.CreateDMChannelAsync();
                if (tldr || pages.Count >= 30)
                {
                    using var stream = GenerateStreamFromString(string.Join("\n", pages.Select(x => x.Description)));
                    await dm.SendFileAsync(stream, "tldr.txt");
                    return ExecutionResult.FromSuccess("lista poszła na PW!");
                }

                if (pages.Count > 0)
                    await dm.SendMessageAsync("", embed: pages[0]);
            }
            catch (Exception ex)
            {
                return DmFailed(ex, $"list of {pages.Count}");
            }

            if (pages.Count > 1)
                LastPagesSending = SendRemainingPagesAsync(dm, pages, delay);

            return ExecutionResult.FromSuccess("lista poszła na PW!");
        }

        private async Task SendRemainingPagesAsync(IDMChannel dm, List<Embed> pages, TimeSpan delay)
        {
            var sent = 1;
            try
            {
                foreach (var emb in pages.Skip(1))
                {
                    await Wait(delay);
                    await dm.SendMessageAsync("", embed: emb);
                    sent++;
                }
            }
            catch (Exception ex)
            {
                if (!IsDmClosed(ex))
                    _log?.LogError($"Helper: DM list stopped after {sent}/{pages.Count} pages: {ex.Message}");
            }
        }

        public const string DmClosed = "nie mogę wysłać Ci wiadomości prywatnej. Włącz wiadomości prywatne od członków tego serwera i spróbuj ponownie.";
        public const string DmFailedText = "nie udało się wysłać wiadomości prywatnej. Spróbuj ponownie za chwilę.";

        public static bool IsDmClosed(Exception ex)
            => ex is Discord.Net.HttpException http && (int?)http.DiscordCode is 50007 or 50278;

        private ExecutionResult DmFailed(Exception ex, string what)
        {
            if (IsDmClosed(ex))
                return ExecutionResult.FromError(DmClosed);

            _log?.LogError($"Helper: DM {what}: {ex}");
            return ExecutionResult.FromError(DmFailedText);
        }

        public async Task<ExecutionResult> SendAsFileOnDMAsync(IUser user, IEnumerable<string> things)
        {
            try
            {
                var dm = await user.CreateDMChannelAsync();
                using (var stream = GenerateStreamFromString(string.Join("\n", things)))
                {
                    await dm.SendFileAsync(stream, "tldr.txt");
                }
                return ExecutionResult.FromSuccess("poszło na PW!");
            }
            catch (Exception ex)
            {
                return DmFailed(ex, "file");
            }
        }

        private Stream GenerateStreamFromString(string str)
        {
            var stream = new MemoryStream();
            using var writer = new StreamWriter(stream, leaveOpen: true);
            writer.Write(str);
            writer.Flush();
            stream.Position = 0;
            return stream;
        }
    }
}