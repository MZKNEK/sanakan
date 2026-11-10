using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Sanakan.Simulation;

public sealed class SimLog
{
    private static readonly Regex Number = new(@"(?<=/)\d+(?=/|$)", RegexOptions.Compiled);

    public ConcurrentDictionary<string, int> Routes { get; } = new();

    public void Info(string message) => Console.WriteLine($"[sim {DateTime.Now:HH:mm:ss}] {message}");

    public void Route(string service, string method, string path)
        => Routes.AddOrUpdate($"{service} {method} {Number.Replace(path.Split('?')[0], "{n}")}", 1, (_, v) => v + 1);
}

// Pairs a simulated command with the first bot message in the same channel that mentions its author.
public sealed class ReplyTracker
{
    private static readonly Regex Mention = new(@"<@!?(\d+)>", RegexOptions.Compiled);

    private readonly ConcurrentDictionary<(ulong Channel, ulong User), ConcurrentQueue<(long Started, string Command)>> _pending = new();

    // Exact: the reply mentioned the author; otherwise it was paired by order in the channel and its time is a guess
    public ConcurrentBag<(string Command, double Ms, bool Exact)> Answered { get; } = new();
    public int Unmatched => _unmatched;
    private int _unmatched;

    public void Sent(ulong channel, ulong user, string command)
        => _pending.GetOrAdd((channel, user), _ => new()).Enqueue((Stopwatch.GetTimestamp(), command));

    public void Replied(ulong channel, string payload)
    {
        foreach (Match match in Mention.Matches(payload ?? ""))
        {
            if (!ulong.TryParse(match.Groups[1].Value, out var user)) continue;
            if (_pending.TryGetValue((channel, user), out var queue) && queue.TryDequeue(out var sent))
            {
                Answered.Add((sent.Command, Stopwatch.GetElapsedTime(sent.Started).TotalMilliseconds, true));
                return;
            }
        }

        // replies without a mention (ping, images) go to the oldest command waiting in the channel
        var oldest = _pending.Where(x => x.Key.Channel == channel && x.Value.TryPeek(out _))
            .OrderBy(x => x.Value.TryPeek(out var first) ? first.Started : long.MaxValue).Select(x => x.Value).FirstOrDefault();
        if (oldest != null && oldest.TryDequeue(out var guessed))
        {
            Answered.Add((guessed.Command, Stopwatch.GetElapsedTime(guessed.Started).TotalMilliseconds, false));
            Interlocked.Increment(ref _byOrder);
            return;
        }
        Interlocked.Increment(ref _unmatched);
    }

    public int MatchedByOrder => _byOrder;
    private int _byOrder;

    public int Waiting => _pending.Values.Sum(x => x.Count);

    public IEnumerable<string> Unanswered => _pending.Values.SelectMany(x => x.Select(y => y.Command));
}
