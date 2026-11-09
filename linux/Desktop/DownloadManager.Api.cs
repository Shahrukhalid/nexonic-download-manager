using Nexonic.Core;

namespace Nexonic.Linux;

/// <summary>The optional local automation API (Options → Automation): what scripts may do with the downloads.</summary>
public sealed partial class DownloadManager : IDownloadControl
{
    LocalApi? api;
    Func<Func<(int, object)>, Task<(int, object)>>? onUi;

    /// <summary>Why the API is not running (empty when it runs or is off).</summary>
    public string ApiProblem { get; private set; } = "";

    /// <summary>Starts or stops the API to match the settings. <paramref name="runOnUi"/> runs a call on the UI thread (kept for later calls).</summary>
    public void ApplyAutomation(Func<Func<(int, object)>, Task<(int, object)>>? runOnUi = null)
    {
        onUi = runOnUi ?? onUi;
        api?.Dispose();
        api = null;
        ApiProblem = "";
        var a = Settings.Automation;
        if (!a.Enabled || onUi == null) return;
        if (a.Key.Length < 16)
        {
            a.Key = AutomationSettings.NewKey();
            ApplySettings(Settings);
        }
        try
        {
            api = new LocalApi(a.Port, a.Key, this, onUi);
            api.Start();
        }
        catch (Exception e)
        {
            api = null;
            ApiProblem = $"Port {a.Port} could not be used: {e.Message}";
        }
    }

    RemoteAccess? remote;

    /// <summary>Why remote access is not running (empty when it runs or is off).</summary>
    public string RemoteProblem { get; private set; } = "";

    /// <summary>The address phones open to pair (with the key in its #fragment), or null when remote access is off.</summary>
    public string? PairingUrl(string? host = null) =>
        remote == null ? null : RemoteAccess.PairingUrl(host ?? RemoteAccess.LanAddresses().FirstOrDefault()?.ToString() ?? "127.0.0.1", remote.Port, Settings.Remote.Key);

    /// <summary>Starts or stops remote access (Options → Remote) to match the settings; a new key unpairs every device.</summary>
    public void ApplyRemote(Func<Func<(int, object)>, Task<(int, object)>>? runOnUi = null, bool newKey = false)
    {
        onUi = runOnUi ?? onUi;
        remote?.Dispose();
        remote = null;
        RemoteProblem = "";
        var r = Settings.Remote;
        if (newKey || (r.Enabled && r.Key.Length < 16))
        {
            r.Key = AutomationSettings.NewKey();
            ApplySettings(Settings);
        }
        if (!r.Enabled || onUi == null) return;
        try
        {
            remote = new RemoteAccess(r.Port, r.Key, this, onUi);
            remote.Start();
        }
        catch (Exception e)
        {
            remote = null;
            RemoteProblem = $"Port {r.Port} could not be used: {e.Message}";
        }
    }

    public IReadOnlyList<DownloadRecord> AllDownloads() => Rows.Select(r => r.Record).ToList();
    public IReadOnlyList<DownloadQueue> AllQueues() => Queues.ToList();

    public List<Guid> AddFromApi(IReadOnlyList<string> urls, string? fileName, string? folder, string? queue, bool start)
    {
        var ids = new List<Guid>();
        foreach (var url in urls)
        {
            var job = new DownloadJob(url, fileName) { Folder = folder, Queue = string.IsNullOrWhiteSpace(queue) ? null : queue.Trim(), Start = start };
            if (Add(job) is Guid id) ids.Add(id);
        }
        return ids;
    }

    Row? RowOf(Guid id) => Rows.FirstOrDefault(r => r.Record.Id == id);

    public bool PauseById(Guid id) { if (RowOf(id) is not { } r) return false; Pause(r); return true; }
    public bool ResumeById(Guid id) { if (RowOf(id) is not { } r) return false; Resume(r); return true; }
    public bool RemoveById(Guid id) { if (RowOf(id) is not { } r) return false; Remove(r); return true; }

    public bool StartQueueById(string id) { if (Queues.All(q => q.Id != id)) return false; StartQueue(id); return true; }
    public bool StopQueueById(string id) { if (Queues.All(q => q.Id != id)) return false; StopQueue(id); return true; }
}
