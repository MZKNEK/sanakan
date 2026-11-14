#pragma warning disable 1591

namespace Sanakan
{
    // What the bot's exit code asks Run.sh and the systemd service (tools/install-service.sh) to do.
    // Any other code, like 134 from an unhandled exception, is treated as a crash and restarted too.
    public static class ExitCodes
    {
        // stopped on purpose (stop command, SIGTERM): stays down
        public const int Stop = 0;

        // something went wrong that a new start can fix (Discord connection lost, API did not start): started again
        public const int Restart = 75;

        // the configuration is invalid: stays down until it is fixed, a restart would only fail again
        public const int BadConfig = 78;

        // the update command: Run.sh pulls, builds and starts the new version
        public const int Update = 100;
    }
}
