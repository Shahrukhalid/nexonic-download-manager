using System.IO;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Nexonic.Core;

namespace Nexonic.App;

/// <summary>
/// Profile Batch for Instagram and Facebook pages: the profile page opens in a hidden, private web view (nothing is kept)
/// and the video links it shows are read with the site's script. With the cookies the extension handed over (the user's
/// own sign-in in their browser) the page is scrolled in that hidden view, a screen every 1.5 seconds, until it shows no
/// more; without them it is what any visitor sees (only the latest posts).
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
        // The window is off screen: the page keeps running and drawing as if it were in front, or it never loads more.
        var env = await CoreWebView2Environment.CreateAsync(null, temp, new CoreWebView2EnvironmentOptions(
            "--disable-features=CalculateNativeWinOcclusion --disable-backgrounding-occluded-windows --disable-renderer-backgrounding --disable-background-timer-throttling"));
        var options = env.CreateCoreWebView2ControllerOptions();
        options.IsInPrivateModeEnabled = true;
        await view.EnsureCoreWebView2Async(env, options);
        return view;
    }

    /// <summary>"name=value; name2=value2" as pairs.</summary>
    static IEnumerable<(string Name, string Value)> Jar(string? header) =>
        (header ?? "").Split(';').Select(p => p.Split('=', 2)).Where(p => p.Length == 2 && p[0].Trim().Length > 0)
            .Select(p => (p[0].Trim(), p[1].Trim()));

    const string ScrollScript = """
        (() => { const v = [...document.querySelectorAll('a[href*="/reel/"], a[href*="/videos/"]')].pop();
        if (v) v.scrollIntoView({block: 'end'}); window.scrollTo(0, document.documentElement.scrollHeight);
        return String(document.documentElement.scrollHeight); })()
        """;

    public static async Task<ProfileBatch.Scan> Scan(string url, SocialSite site, string owner, string[] pages, int max, Action<int> found,
        CancellationToken cancel, string? cookies = null, string? userAgent = null, List<BatchEntry>? seed = null)
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
            var web = view.CoreWebView2;
            var signedIn = false;
            foreach (var (name, value) in Jar(cookies))
            {
                var c = web.CookieManager.CreateCookie(name, value, "." + site.Domain, "/");
                c.IsSecure = true;
                web.CookieManager.AddOrUpdateCookie(c);
                signedIn = true;
            }
            // The browser the sign-in came from, as the site knows it.
            if (!string.IsNullOrWhiteSpace(userAgent)) web.Settings.UserAgent = userAgent;
            // The sites load more posts only for a page that is being looked at.
            await web.AddScriptToExecuteOnDocumentCreatedAsync("""
                Object.defineProperty(document, 'visibilityState', { get: () => 'visible' });
                Object.defineProperty(document, 'hidden', { get: () => false });
                document.hasFocus = () => true;
                """);
            var all = new Dictionary<string, BatchEntry>();
            SocialPages.Merge(all, seed ?? new List<BatchEntry>(), max);
            var posts = 0;
            foreach (var page in pages)
            {
                web.Navigate(page);
                // Read until the page stops changing: signed in, scrolling down a screen at a time (a person's pace) until
                // nothing new comes for a while; a visitor gets no more by scrolling.
                var height = "";
                for (int i = 0, idle = 0; i < (signedIn ? 1200 : 25) && idle < (signedIn ? 20 : 2) && all.Count < max; i++)
                {
                    await Task.Delay(signedIn ? 1500 : 1000, cancel);
                    string json;
                    try { json = await web.ExecuteScriptAsync(site.Script); }
                    catch (Exception x) when (x is InvalidOperationException or System.Runtime.InteropServices.COMException) { continue; }
                    var now = InstagramPage.Read(json);
                    posts = Math.Max(posts, now.Posts);
                    var grew = SocialPages.Merge(all, now.Videos, max);
                    idle = grew || i < 6 ? 0 : idle + 1;  // the page's first seconds are for loading
                    if (grew) found(all.Count);
                    if (!signedIn) continue;
                    var before = height;
                    try { height = await web.ExecuteScriptAsync(ScrollScript); }
                    catch (Exception x) when (x is InvalidOperationException or System.Runtime.InteropServices.COMException) { }
                    if (height != before) idle = 0;  // still loading: the page is getting longer
                }
            }
            if (all.Count == 0)
                throw new InvalidOperationException(posts == 0 && signedIn ? $"{site.Name} showed no videos on this page, even with your browser's sign-in."
                    : posts == 0 ? $"{site.Name} did not show any videos of this profile to a visitor who is not signed in (it may be private or not exist)."
                    : InstagramPage.NoVideos(posts));
            var note = signedIn ? "Read with your browser's sign-in, scrolling the page in the background until it showed no more."
                : $"{site.Name} shows visitors who are not signed in only the latest posts; Nexonic took the videos among them.";
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
