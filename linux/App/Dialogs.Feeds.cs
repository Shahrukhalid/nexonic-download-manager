using Avalonia.Controls;
using Avalonia.Media;
using Nexonic.Core;
using Nexonic.Linux;

namespace Nexonic.App;

/// <summary>RSS feeds with download rules, the same as the Windows app's.</summary>
public static partial class Dialogs
{
    public static async Task Feeds(Window owner, DownloadManager m, FeedRunner runner, Func<FeedSubscription, FeedItem, Task> add)
    {
        var f = new Form(owner, "RSS Feeds", 680);
        f.Note("New items of these feeds are downloaded when their title matches the rule: files and videos, and torrents when torrents are on. " +
               "A rule lists words that must all be in the title, or a /regular expression/.");
        var list = f.Field("Feeds", new ListBox { Height = 220 });
        var status = f.Note("");
        void Fill()
        {
            lock (runner.Subscriptions)
                list.ItemsSource = runner.Subscriptions.Select(s => $"{(s.Enabled ? "" : "(off) ")}{(s.Name.Length > 0 ? s.Name : s.Url)}" +
                    $"   ·   {(s.Include.Length > 0 ? s.Include : "every item")}   ·   {(s.LastChecked is { } t ? t.ToLocalTime().ToString("g") + ": " + s.LastResult : "not checked yet")}").ToList();
        }
        Fill();
        FeedSubscription? Selected() { lock (runner.Subscriptions) return list.SelectedIndex >= 0 && list.SelectedIndex < runner.Subscriptions.Count ? runner.Subscriptions[list.SelectedIndex] : null; }
        async Task Edit(FeedSubscription? existing)
        {
            var feed = existing ?? new FeedSubscription();
            if (await EditFeed(f.Window, m, feed))
            {
                lock (runner.Subscriptions) if (existing == null) runner.Subscriptions.Add(feed);
                runner.Save();
                Fill();
            }
        }
        f.AddButton("Add Feed…", () => _ = Edit(null));
        f.AddButton("Edit…", () => { if (Selected() is { } s) _ = Edit(s); });
        f.AddButton("Remove", () =>
        {
            if (Selected() is not { } s) return;
            lock (runner.Subscriptions) runner.Subscriptions.Remove(s);
            runner.Save();
            Fill();
        });
        f.AddButton("Check Now", async () =>
        {
            status.Text = "Checking…";
            List<FeedSubscription> all;
            lock (runner.Subscriptions) all = runner.Subscriptions.Where(s => s.Enabled).ToList();
            await runner.CheckAsync(m.Settings.Proxy, add, all);
            status.Text = "";
            Fill();
        });
        f.AddButton("Close", f.Close, isDefault: true, isCancel: true);
        await f.Show();
    }

    static async Task<bool> EditFeed(Window owner, DownloadManager m, FeedSubscription feed)
    {
        var f = new Form(owner, feed.Url.Length == 0 ? "Add Feed" : "Edit Feed", 560);
        var url = f.Text("Feed address (RSS or Atom)", feed.Url);
        var name = f.Text("Name (optional)", feed.Name);
        var include = f.Text("Download items whose title has all of these words (empty = every item)", feed.Include);
        var exclude = f.Text("Skip items whose title has any of these words", feed.Exclude);
        var folder = f.Folder(feed.Folder.Length > 0 ? feed.Folder : m.Settings.Folder);
        var interval = f.Text("Check every (minutes, 15 or more)", feed.IntervalMinutes.ToString());
        var skip = f.Check("Only download items that appear after the feed is added", feed.SkipExisting);
        var on = f.Check("Check this feed", feed.Enabled);
        var error = f.Error();
        var saved = false;
        f.AddButton("Cancel", f.Close, isCancel: true);
        f.AddButton("Save", () =>
        {
            var u = (url.Text ?? "").Trim();
            if (!Uri.TryCreate(u, UriKind.Absolute, out var parsed) || parsed.Scheme is not ("http" or "https")) { error.Text = "The feed address must start with http:// or https://"; return; }
            if ((Nexonic.Core.Feeds.RuleProblem(include.Text ?? "") ?? Nexonic.Core.Feeds.RuleProblem(exclude.Text ?? "")) is { } problem) { error.Text = problem; return; }
            if (feed.Url != u) { feed.Seen.Clear(); feed.LastChecked = null; }
            feed.Url = u;
            feed.Name = (name.Text ?? "").Trim();
            feed.Include = (include.Text ?? "").Trim();
            feed.Exclude = (exclude.Text ?? "").Trim();
            var dir = (folder.Text ?? "").Trim();
            feed.Folder = dir == m.Settings.Folder ? "" : dir;
            feed.IntervalMinutes = Math.Clamp(Ui.Number(interval.Text, 30), 15, 1440);
            feed.SkipExisting = skip.IsChecked == true;
            feed.Enabled = on.IsChecked == true;
            saved = true;
            f.Close();
        }, isDefault: true);
        await f.Show();
        return saved;
    }
}
