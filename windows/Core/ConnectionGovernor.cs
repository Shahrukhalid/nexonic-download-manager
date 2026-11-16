namespace Nexonic.Core;

/// <summary>
/// How many connections a download may use right now. Auto (the default) starts with a few, adds more while the measured
/// speed keeps rising and stops adding when it no longer does; any mode backs off when the server refuses (HTTP 429/503,
/// refused or reset connections) and waits as long as the server asks (Retry-After) before growing again.
/// </summary>
public sealed class ConnectionGovernor
{
    public const int AutoStart = 4;
    public static readonly TimeSpan Sample = TimeSpan.FromMilliseconds(500);
    /// <summary>Refusals this close together are one answer from the server ("too many"), not several.</summary>
    public static readonly TimeSpan Burst = TimeSpan.FromSeconds(2);

    readonly int ceiling;
    readonly bool auto;
    readonly object gate = new();
    int allowed;
    long bytes;
    DateTime sampleStart;
    double bestRate;
    int flat;
    DateTime quietUntil;
    int refusedAt = int.MaxValue;  // the number of connections a server refused at: Auto stays below it
    DateTime burstStart = DateTime.MinValue;
    int burstBase;     // connections allowed when the current burst of refusals began
    int burstCount;    // refusals in that burst
    int proven;        // the most connections that recently downloaded together without a refusal
    int burstFloor;    // what proven was when the current burst began: the burst cannot go below it

    /// <param name="auto">Auto: grow from <see cref="AutoStart"/> up to <paramref name="maximum"/>; else use <paramref name="maximum"/>.</param>
    public ConnectionGovernor(bool auto, int maximum, DateTime now)
    {
        this.auto = auto;
        ceiling = Math.Clamp(maximum, 1, Limits.MaxConnections);
        allowed = auto ? Math.Min(AutoStart, ceiling) : ceiling;
        sampleStart = now;
    }

    public int Allowed { get { lock (gate) return allowed; } }

    /// <summary>Until then the server asked to wait: no new connections are opened.</summary>
    public DateTime QuietUntil { get { lock (gate) return quietUntil; } }

    public void Received(long n) { lock (gate) bytes += n; }

    /// <summary>Called regularly: at the end of each sample (half a second), Auto doubles the connections while the speed is still
    /// rising (by at least 10 %), stays below a number the server refused, and stops when the speed no longer rises. A fixed
    /// setting slowly returns to its number after a back-off.</summary>
    public void Tick(DateTime now, int running)
    {
        lock (gate)
        {
            if (now - sampleStart < Sample) return;
            var rate = bytes / Math.Max(0.001, (now - sampleStart).TotalSeconds);
            // A whole sample with data flowing and no refusal: this many connections work with this server.
            if (rate > 0 && now - burstStart >= Sample * 2) proven = Math.Max(proven, Math.Min(running, allowed));
            bytes = 0;
            sampleStart = now;
            if (now < quietUntil) return;
            if (!auto) { if (allowed < ceiling) allowed = Math.Min(ceiling, allowed + Math.Max(1, allowed / 4)); return; }
            if (running < allowed) return;  // not every allowed connection is busy yet: the sample says nothing
            if (rate > bestRate * 1.10)
            {
                bestRate = rate;
                flat = 0;
                allowed = Math.Max(allowed, Math.Min(Math.Min(ceiling, refusedAt - 1), allowed * 2));
            }
            else if (++flat >= 3) flat = 3;  // no gain: stay at this number
        }
    }

    /// <summary>
    /// The server refused a connection. Refusals that arrive together (within <see cref="Burst"/>) are one answer: each extra
    /// one means one connection fewer is welcome. Auto then uses exactly the number that was accepted (one below the first
    /// refused) instead of halving, so a server that allows 6 connections gets 6, not 3. A fixed setting halves, as before.
    /// Either way no new connection opens until the server's wait is over (30 s when it did not say, at most 10 minutes).
    /// </summary>
    public void Refused(DateTime now, TimeSpan? retryAfter)
    {
        lock (gate)
        {
            var sameBurst = now >= burstStart && now - burstStart < Burst;
            if (sameBurst) burstCount++;
            else
            {
                burstStart = now; burstBase = allowed; burstCount = 1;
                burstFloor = auto ? proven : 0;
                proven = 0;  // must work again before it protects another burst
            }
            var accepted = Math.Max(1, burstBase - burstCount);  // the connections that were not refused
            // Parts finishing together reconnect together, and a busy server may refuse a few of them at once. A number of
            // connections that has just worked is kept once; if the server refuses again soon after, the lower number holds.
            if (burstFloor > accepted) accepted = Math.Max(1, Math.Min(burstFloor, refusedAt - 1));
            refusedAt = Math.Min(refusedAt, accepted + 1);
            if (auto) allowed = Math.Min(allowed, accepted);
            else if (!sameBurst) allowed = Math.Max(1, allowed / 2);
            var wait = retryAfter is { } r ? TimeSpan.FromSeconds(Math.Clamp(r.TotalSeconds, 1, 600)) : TimeSpan.FromSeconds(30);
            if (now + wait > quietUntil) quietUntil = now + wait;
            bestRate = 0;  // measure again at the new number
            flat = 0;
        }
    }

    /// <summary>Retry-After as seconds or as a date; null when absent or unreadable.</summary>
    public static TimeSpan? RetryAfter(System.Net.Http.Headers.RetryConditionHeaderValue? h, DateTime nowUtc) =>
        h?.Delta is { } d ? d
        : h?.Date is { } at ? at.UtcDateTime - nowUtc is var left && left > TimeSpan.Zero ? left : TimeSpan.Zero
        : null;

    /// <summary>Statuses that mean "too many connections, slow down" rather than "this file is broken".</summary>
    public static bool IsRefusal(System.Net.HttpStatusCode code) => code is System.Net.HttpStatusCode.TooManyRequests
        or System.Net.HttpStatusCode.ServiceUnavailable or (System.Net.HttpStatusCode)509;
}

/// <summary>The server asked this connection to stop (429/503): its part goes back to the waiting list, nothing is lost.</summary>
public sealed class RefusedException(TimeSpan? retryAfter, int status) : IOException($"The server is busy (HTTP {status})")
{
    public TimeSpan? RetryAfter { get; } = retryAfter;
}
