using System.IO;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Nexonic.Core;

namespace Nexonic.App;

/// <summary>
/// Profile Batch for Instagram and Facebook: the profile page opens in a hidden web view, as in a visitor's browser, and
/// the video links it shows are read with the site's script. Without signing in the view is private (no cookies) and the
/// site shows only the latest posts. After the optional sign-in (on the site's own page, in Nexonic's own session folder
/// on this computer) the page is scrolled to the end and every video is found.
/// </summary>
static class SocialWebView
{
    static string Folder(SocialSite site) => Path.Combine(AppPaths.Support, "WebSessions", site.Name.ToLowerInvariant());
    static string Mark(SocialSite site) => Path.Combine(AppPaths.Support, "WebSessions", site.Name.ToLowerInvariant() + ".signed-in");
    public static bool SignedIn(SocialSite site) => File.Exists(Mark(site));

    static void RequireRuntime()
    {
        try { _ = CoreWebView2Environment.GetAvailableBrowserVersionString(); }
        catch (WebView2RuntimeNotFoundException)
        {
            throw new InvalidOperationException("Instagram and Facebook profiles open in the Microsoft Edge WebView2 Runtime, which is not installed on this "
                + "computer. Install it from Microsoft, or open the profile in your browser, right-click the page and choose \"Download all links with Nexonic\".");
        }
    }

    static async Task<WebView2> Open(Window window, bool signedIn, SocialSite site, string temp)
    {
        var view = new WebView2();
        window.Content = view;
        var env = await CoreWebView2Environment.CreateAsync(null, signedIn ? Folder(site) : temp);
        var options = env.CreateCoreWebView2ControllerOptions();
        options.IsInPrivateModeEnabled = !signedIn;
        await view.EnsureCoreWebView2Async(env, options);
        return view;
    }

    public static async Task<ProfileBatch.Scan> Scan(string url, SocialSite site, string owner, string[] pages, int max, Action<int> found,
        CancellationToken cancel)
    {
        RequireRuntime();
        var signedIn = SignedIn(site);
        var temp = Path.Combine(Path.GetTempPath(), "nexonic-page-" + Guid.NewGuid().ToString("N"));
        var window = new Window
        {
            Width = 1100, Height = 1600, Left = -20000, Top = -20000, WindowStyle = WindowStyle.None, ShowInTaskbar = false, ShowActivated = false,
        };
        window.Show();
        WebView2? view = null;
        try
        {
            view = await Open(window, signedIn, site, temp);
            var all = new Dictionary<string, BatchEntry>();
            var posts = 0;
            foreach (var page in pages)
            {
                view.CoreWebView2.Navigate(page);
                // Signed in: scroll until nothing new comes for a while. Signed out: read until the page stops changing.
                for (int i = 0, idle = 0; i < (signedIn ? 2400 : 25) && idle < (signedIn ? 5 : 2) && all.Count < max; i++)
                {
                    await Task.Delay(signedIn ? 1500 : 1000, cancel);
                    string json;
                    try { json = await view.CoreWebView2.ExecuteScriptAsync(site.Script); }
                    catch (Exception x) when (x is InvalidOperationException or System.Runtime.InteropServices.COMException) { continue; }
                    var now = InstagramPage.Read(json);
                    posts = Math.Max(posts, now.Posts);
                    var grew = SocialPages.Merge(all, now.Videos, max);
                    idle = grew || (all.Count == 0 && i < 10) ? 0 : idle + 1;
                    if (grew) found(all.Count);
                    if (signedIn) try { await view.CoreWebView2.ExecuteScriptAsync(SocialPages.Scroll); } catch (InvalidOperationException) { }
                }
            }
            if (all.Count == 0)
                throw new InvalidOperationException(posts == 0 ? $"{site.Name} did not show any videos of this profile" + (signedIn ? "." : " to a visitor who is not signed in (it may be private or not exist).")
                    : InstagramPage.NoVideos(posts));
            var note = signedIn ? SocialPages.SignedInNote(site, all.Count) : SocialPages.OfferSignIn(site);
            return new ProfileBatch.Scan { Url = url, Owner = owner, Entries = all.Values.ToList(), Complete = true, Note = note };
        }
        finally
        {
            window.Close();
            view?.Dispose();
            try { Directory.Delete(temp, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>
    /// The site's own sign-in page in a window of Nexonic's: the password goes to the site, never to Nexonic. Returns true once
    /// the site has signed the user in (its session cookie is there); the window then closes by itself.
    /// </summary>
    public static async Task<bool> SignIn(Window owner, SocialSite site)
    {
        RequireRuntime();
        Directory.CreateDirectory(Folder(site));
        var window = new Window { Title = $"Sign in to {site.Name} (Nexonic never sees your password)", Width = 520, Height = 760, Owner = owner,
            WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var closed = new TaskCompletionSource();
        window.Closed += (_, _) => closed.TrySetResult();
        window.Show();
        var view = await Open(window, true, site, "");
        view.CoreWebView2.Navigate(site.LoginUrl);
        var done = false;
        while (!closed.Task.IsCompleted)
        {
            await Task.WhenAny(closed.Task, Task.Delay(1500));
            if (closed.Task.IsCompleted) break;
            var cookies = await view.CoreWebView2.CookieManager.GetCookiesAsync("https://www." + site.Domain);
            if (cookies.Any(c => c.Name == site.SessionCookie && c.Value.Length > 0))
            {
                await File.WriteAllTextAsync(Mark(site), DateTime.Now.ToString("O"));
                done = true;
                window.Close();
            }
        }
        view.Dispose();
        return done;
    }

    /// <summary>Removes Nexonic's session for the site (the site's own account is untouched).</summary>
    public static void SignOut(SocialSite site)
    {
        try { File.Delete(Mark(site)); } catch (IOException) { }
        try { Directory.Delete(Folder(site), true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
