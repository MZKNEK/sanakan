#pragma warning disable 1591

using System;
using System.Collections.Generic;
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

        public IEnumerable<ModuleInfo> PublicModulesInfo { get; set; }
        public Dictionary<string, ModuleInfo> PrivateModulesInfo { get; set; }

        public Helper(IConfig config)
        {
            _config = config;
            _httpClient = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });

            PublicModulesInfo = new List<ModuleInfo>();
            PrivateModulesInfo = new Dictionary<string, ModuleInfo>();
        }

        public async Task<HttpStatusCode> GetResponseFromUrl(string url)
            => (await _httpClient.GetAsync(url)).StatusCode;

        public string GivePublicHelp()
        {
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

            sb.AppendLine($"\n\nUżyj `{_config.Get().Prefix}pomoc [polecenie]`, aby uzyskać informacje dotyczące danego polecenia.");

            return sb.ToString();
        }

        public string GivePrivateHelp(string moduleName)
        {
            var moduleInfo = GetInfoAboutModule(PrivateModulesInfo[moduleName]);
            return $"**Lista poleceń:**\n\n**{moduleInfo.Prefix}:** " + string.Join("  ", moduleInfo.Commands);
        }

        public string GiveHelpAboutPrivateCmd(string moduleName, string command, string prefix, bool throwEx = true)
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

        public string GetCommandInfo(CommandInfo cmd, string prefix = null)
        {
            string modulePrefix = GetModGroupPrefix(cmd.Module);
            string botPrefix = prefix ?? _config.Get().Prefix;

            var commandBuilder = new StringBuilder($"**{botPrefix}{modulePrefix}{cmd.Name}**");

            if (cmd.Parameters.Any())
            {
                foreach (var param in cmd.Parameters.Where(param => !param.IsHidden()))
                {
                    commandBuilder.Append($" `{param.Name}` ");
                }
            }

            commandBuilder.Append($" - {cmd.Summary}\n");

            if (cmd.Parameters.Any())
            {
                foreach (var param in cmd.Parameters.Where(param => !param.IsHidden()))
                {
                    commandBuilder.Append($"*{param.Name}* - *{param.Summary}*\n");
                }
            }

            if (cmd.Aliases.Count > 1)
            {
                commandBuilder.Append("\n**Aliasy:**\n");
                foreach (var alias in cmd.Aliases.Where(alias => alias != cmd.Name))
                {
                    commandBuilder.Append($"`{alias}` ");
                }
            }

            commandBuilder.Append($"\n\nnp. `{botPrefix}{modulePrefix}{cmd.Name} {cmd.Remarks}`");

            return commandBuilder.ToString();
        }

        public string GiveHelpAboutPublicCmd(string command, string prefix, bool admin = false, bool dev = false)
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
                if (!string.IsNullOrEmpty(res)) return res;
            }

            if (dev)
            {
                var res = GiveHelpAboutPrivateCmd("Debug", command, prefix, false);
                if (!string.IsNullOrEmpty(res)) return res;
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
                        .Where(x => !string.IsNullOrEmpty(x.Name))
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
                    .Where(cmd => !string.IsNullOrEmpty(cmd.Name))
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
                roles = string.Join("\n", user.Roles
                    .OrderByDescending(x => x.Position)
                    .Where(x => !x.IsEveryone)
                    .Select(x => x.Mention));
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
                    Name = "Pseudo",
                    Value = user.Nickname ?? "Brak",
                    IsInline = true
                },
                new EmbedFieldBuilder
                {
                    Name = "Status",
                    Value = user.Status.ToString(),
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
                    Value = user.CreatedAt.ToString("dd.MM.yyyy HH:mm:ss"),
                    IsInline = false
                },
                new EmbedFieldBuilder
                {
                    Name = "Dołączono",
                    Value = user.JoinedAt?.ToString("dd.MM.yyyy HH:mm:ss") ?? "Nieznane",
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
                    Value = guild.CreatedAt.ToString("dd.MM.yyyy HH:mm:ss"),
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
            foreach (ITextChannel channel in guild.Channels)
            {
                if (channel == null)
                    continue;

                IMessage msg = await channel.GetMessageAsync(id);
                if (msg != null)
                {
                    return msg;
                }
            }
            return null;
        }

        public Embed BuildRaportInfo(IMessage message, string reportAuthor, string reason, ulong reportId)
        {
            var attachments = message.Attachments.Select(x => x.Url).ToList();
            var attach = attachments.Count > 0 ? string.Join("\n", attachments) : "brak";

            return new EmbedBuilder
            {
                Footer = new EmbedFooterBuilder().WithText($"Zgłasza: {reportAuthor}".TrimToLength(EmbedFooterBuilder.MaxFooterTextLength)),
                Description = message.Content?.TrimToLength(1500) ?? "sam załącznik",
                Author = new EmbedAuthorBuilder().WithUser(message.Author),
                Color = EMType.Error.Color(),
                Fields = new List<EmbedFieldBuilder>
                {
                    new EmbedFieldBuilder
                    {
                        IsInline = true,
                        Name = "Kanał:",
                        Value = message.Channel.Name,
                    },
                    new EmbedFieldBuilder
                    {
                        IsInline = true,
                        Name = "Napisano:",
                        Value = $"{message.GetLocalCreatedAtShortDateTime()}"
                    },
                    new EmbedFieldBuilder
                    {
                        IsInline = true,
                        Name = "Id zgloszenia:",
                        Value = reportId.ToString()
                    },
                    new EmbedFieldBuilder
                    {
                        IsInline = false,
                        Name = "Powód:",
                        Value = reason.TrimToLength(EmbedFieldBuilder.MaxFieldValueLength)
                    },
                    new EmbedFieldBuilder
                    {
                        IsInline = false,
                        Name = "Załączniki:",
                        Value = attach.TrimToLength(EmbedFieldBuilder.MaxFieldValueLength)
                    }
                }
            }.Build();
        }

        public async Task<ExecutionResult> SendEmbedsOnDMAsync(IUser user, IEnumerable<Embed> embeds)
            => await SendEmbedsOnDMAsync(user, embeds, TimeSpan.FromSeconds(2));

        public async Task<ExecutionResult> SendEmbedsOnDMAsync(IUser user, IEnumerable<Embed> embeds, TimeSpan delay)
        {
            try
            {
                var dm = await user.CreateDMChannelAsync();
                foreach (var emb in embeds)
                {
                    await dm.SendMessageAsync("", embed: emb);
                    await Task.Delay(delay);
                }
                return ExecutionResult.FromSuccess("lista poszła na PW!");
            }
            catch (Exception)
            {
                return ExecutionResult.FromError("nie można wysłać do Ciebie PW!");
            }
        }
    }
}