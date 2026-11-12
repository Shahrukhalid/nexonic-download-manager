using System.IO;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Nexonic.Core;

namespace Nexonic.App;

/// <summary>
/// Profile Batch for Instagram and Facebook without the browser extension: the profile page opens in a hidden, private
/// web view (no cookies, no sign-in: what any visitor sees) and the video links it shows are read with the site's script.
/// The sites show visitors only their latest posts; every video comes through the user's own browser (BrowserCollect).
/// </summary>
static class SocialWebView
{
    static void RequireRuntime()
    {
        try { _ = CoreWebView2Environment.GetAvailableBrowserVersionString(); }
        catch (WebView2RuntimeNotFoundException)
        {
            throw new InvalidOperationException("Instagram and Facebook profiles open in the Microsoft Edge WebView2 Runtime, which is not installed on this "
                + "computer. Install it from Microsoft, or open the profile in your browser, right-click the page and choose \"Download all links with Nexonic\".");
        }
    }

    static async Task<WebView2> Open(Window window, string temp)
    {
        var view = new WebView2();
        window.Content = view;
        var env = await CoreWebView2Environment.CreateAsync(null, temp);
        var options = env.CreateCoreWebView2ControllerOptions();
        options.IsInPrivateModeEnabled = true;
        await view.EnsureCoreWebView2Async(env, options);
        return view;
    }

    public static async Task<ProfileBatch.Scan> Scan(string url, SocialSite site, string owner, string[] pages, int max, Action<int> found,
        CancellationToken cancel)
    {
        RequireRuntime();
        var temp = Path.Combine(Path.GetTempPath(), "nexonic-page-" + Guid.NewGuid().ToString("N"));
        var window = new Window
        {
            Width = 1100, Height = 1600, Left = -20000, Top = -20000, WindowStyle = WindowStyle.None, ShowInTaskbar = false, ShowActivated = false,
        };
        window.Show();
        WebView2? view = null;
        try
        {
            view = await Open(window, temp);
            var all = new Dictionary<string, BatchEntry>();
            var posts = 0;
            foreach (var page in pages)
            {
                view.CoreWebView2.Navigate(page);
                // Read until the page stops changing (visitors get no more by scrolling).
                for (int i = 0, idle = 0; i < 25 && idle < 2 && all.Count < max; i++)
                {
                    await Task.Delay(1000, cancel);
                    string json;
                    try { json = await view.CoreWebView2.ExecuteScriptAsync(site.Script); }
                    catch (Exception x) when (x is InvalidOperationException or System.Runtime.InteropServices.COMException) { continue; }
                    var now = InstagramPage.Read(json);
                    posts = Math.Max(posts, now.Posts);
                    var grew = SocialPages.Merge(all, now.Videos, max);
                    idle = grew || (all.Count == 0 && i < 10) ? 0 : idle + 1;
                    if (grew) found(all.Count);
                }
            }
            if (all.Count == 0)
                throw new InvalidOperationException(posts == 0 ? $"{site.Name} did not show any videos of this profile to a visitor who is not signed in (it may be private or not exist)."
                    : InstagramPage.NoVideos(posts));
            var note = $"{site.Name} shows visitors who are not signed in only the latest posts; Nexonic took the videos among them.";
            return new ProfileBatch.Scan { Url = url, Owner = owner, Entries = all.Values.ToList(), Complete = true, Note = note };
        }
        finally
        {
            window.Close();
            view?.Dispose();
            try { Directory.Delete(temp, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
