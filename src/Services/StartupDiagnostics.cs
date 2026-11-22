#pragma warning disable 1591

using System;

namespace Sanakan.Services
{
    public static class StartupDiagnostics
    {
        public static readonly TimeSpan DiscordReadyWarning = TimeSpan.FromSeconds(60);
        public static readonly TimeSpan SlowStepWarning = TimeSpan.FromSeconds(15);

        public static string SlowStep(string step, TimeSpan elapsed)
            => $"Start: {step} still running after {elapsed.TotalSeconds:0} s";

        public static int? GatewayCloseCode(Exception ex)
        {
            for (var e = ex; e != null; e = e.InnerException)
                if (e is Discord.Net.WebSocketClosedException closed)
                    return closed.CloseCode;

            return null;
        }

        // https://discord.com/developers/docs/topics/opcodes-and-status-codes#gateway-gateway-close-event-codes
        public static string GatewayCloseHint(int? code) => code switch
        {
            4004 => "the bot token is wrong (BotToken in the config)",
            4013 => "the bot asks for intents Discord does not know",
            4014 => "privileged intents are off: turn on Server Members Intent and Message Content Intent in the Discord Developer Portal (Bot tab)",
            _ => null,
        };

        public static string DescribeDisconnect(Exception ex)
        {
            if (ex == null)
                return "Discord: disconnected before the bot was ready";

            var code = GatewayCloseCode(ex);
            var hint = GatewayCloseHint(code);
            var text = $"Discord: disconnected before the bot was ready{(code == null ? "" : $" (close code {code})")}: {ex.Message}";
            return hint == null ? text : $"{text} - {hint}";
        }

        public static string NotReadyWarning(TimeSpan waited)
            => $"Start: still not connected to Discord after {waited.TotalSeconds:0} s - check the token, the privileged intents "
                + "(Server Members, Message Content) in the Developer Portal and the connection to Discord";
    }
}
