using System.Diagnostics;
using System.Management;

namespace Sanakan.Simulation;

// CPU time of all local processes with a given name (mysqld runs as two processes on Windows).
// A Windows service's processes cannot be opened by a normal user, so their times come from WMI there.
public sealed class ProcessCpu
{
    private readonly string _name;
    private bool _useWmi;

    private ProcessCpu(string name) => _name = name;

    public static ProcessCpu Find(string name)
    {
        var cpu = new ProcessCpu(name);
        return cpu.Total() != null ? cpu : null;
    }

    public TimeSpan? Total()
    {
        if (!_useWmi)
        {
            var processes = Process.GetProcessesByName(_name);
            try
            {
                if (processes.Length == 0)
                    return null;
                var total = TimeSpan.Zero;
                foreach (var process in processes)
                    total += process.TotalProcessorTime;
                return total;
            }
            catch (Exception) when (OperatingSystem.IsWindows())
            {
                _useWmi = true;
            }
            finally
            {
                foreach (var process in processes)
                    process.Dispose();
            }
        }

        if (!OperatingSystem.IsWindows())
            return null;

        using var searcher = new ManagementObjectSearcher($"SELECT UserModeTime, KernelModeTime FROM Win32_Process WHERE Name = '{_name}.exe'");
        using var results = searcher.Get();
        long ticks = 0;
        var found = false;
        foreach (var item in results)
        {
            using (item)
            {
                ticks += (long)(ulong)item["UserModeTime"] + (long)(ulong)item["KernelModeTime"];
                found = true;
            }
        }
        return found ? TimeSpan.FromTicks(ticks) : null;
    }
}
