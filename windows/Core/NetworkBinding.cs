using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Nexonic.Core;

/// <summary>The adapter is missing or down; downloads stop instead of using another connection.</summary>
public sealed class AdapterDownException(string adapter)
    : HttpRequestException($"The network adapter \"{adapter}\" is not connected. Downloads wait for it instead of using another connection.");

/// <summary>
/// Keeps downloads on one network adapter (a VPN, for example). Every connection is bound to the adapter's own
/// address, so when it goes down nothing falls back to another adapter: that is the kill switch.
/// </summary>
public static class Adapters
{
    public sealed record Adapter(string Name, string Description, bool Up, IReadOnlyList<IPAddress> Addresses)
    {
        public string Title => Description.Length > 0 && Description != Name ? $"{Name} ({Description})" : Name;
    }

    /// <summary>Adapters with at least one usable address, loopback last.</summary>
    public static List<Adapter> List()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Select(n => new Adapter(n.Name, n.Description, n.OperationalStatus is OperationalStatus.Up or OperationalStatus.Unknown,
                    Usable(n).ToList()))
                .Where(a => a.Addresses.Count > 0)
                .OrderBy(a => a.Addresses.All(IPAddress.IsLoopback)).ThenBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (NetworkInformationException) { return new(); }
    }

    static IEnumerable<IPAddress> Usable(NetworkInterface n)
    {
        IPInterfaceProperties props;
        try { props = n.GetIPProperties(); } catch (NetworkInformationException) { yield break; }
        foreach (var u in props.UnicastAddresses)
        {
            var ip = u.Address;
            if (ip.AddressFamily == AddressFamily.InterNetworkV6 && (ip.IsIPv6LinkLocal || ip.IsIPv6Multicast)) continue;
            if (ip.AddressFamily is AddressFamily.InterNetwork or AddressFamily.InterNetworkV6) yield return ip;
        }
    }

    /// <summary>Options' adapter list: "" (any adapter) first, then every adapter, keeping a saved one that is offline.</summary>
    public static List<string> Choices(string current)
    {
        var names = new List<string> { "" };
        names.AddRange(List().Select(a => a.Name));
        if (current.Trim().Length > 0 && !names.Contains(current.Trim(), StringComparer.OrdinalIgnoreCase)) names.Add(current.Trim());
        return names;
    }

    /// <summary>A problem with the blocklist setting for Options, or null.</summary>
    public static string? BlocklistProblem(string? setting)
    {
        var s = (setting ?? "").Trim();
        if (s.Length == 0) return null;
        if (Uri.TryCreate(s, UriKind.Absolute, out var u) && u.Scheme is "http" or "https")
            return u.Scheme == Uri.UriSchemeHttps ? null : "The blocklist address must start with https://";
        return Path.IsPathFullyQualified(s) && File.Exists(s) ? null : "The blocklist file was not found.";
    }

    /// <summary>The adapter's addresses (IPv4 first), or AdapterDownException when it is missing, down or has none.</summary>
    public static IReadOnlyList<IPAddress> AddressesOf(string adapter, IEnumerable<Adapter>? all = null)
    {
        var name = adapter.Trim();
        var found = (all ?? List()).FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));
        if (found is not { Up: true } || found.Addresses.Count == 0) throw new AdapterDownException(name);
        return found.Addresses.OrderBy(a => a.AddressFamily == AddressFamily.InterNetworkV6).ToList();
    }

    /// <summary>The address torrents and the video engine use for this adapter (its first IPv4, else IPv6).</summary>
    public static string SourceAddress(string adapter) => AddressesOf(adapter)[0].ToString();

    /// <summary>
    /// Connects to `target` from the adapter's address of the same family. Names are looked up with `resolve`.
    /// Addresses the adapter can't reach (another family) are skipped; nothing is ever sent from another adapter.
    /// </summary>
    public static async ValueTask<Stream> ConnectAsync(IReadOnlyList<IPAddress>? local, DnsEndPoint target,
        Func<string, CancellationToken, Task<IPAddress[]>> resolve, TimeSpan timeout, CancellationToken ct)
    {
        var remote = IPAddress.TryParse(target.Host, out var literal) ? new[] { literal } : await resolve(target.Host, ct);
        Exception? last = null;
        foreach (var ip in remote.OrderBy(a => a.AddressFamily == AddressFamily.InterNetworkV6))
        {
            var from = local?.FirstOrDefault(l => l.AddressFamily == ip.AddressFamily);
            if (local != null && from == null) continue;
            var socket = new Socket(ip.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                if (from != null) socket.Bind(new IPEndPoint(from, 0));
                using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
                limit.CancelAfter(timeout);
                await socket.ConnectAsync(new IPEndPoint(ip, target.Port), limit.Token);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (Exception e) when (!ct.IsCancellationRequested) { socket.Dispose(); last = e; }
        }
        if (local != null && last == null)
            throw new HttpRequestException($"{target.Host} has no address the chosen network adapter can reach.");
        throw new HttpRequestException($"Could not connect to {target.Host}", last);
    }
}
