using System.IO;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Nexonic.Core;

namespace Nexonic.App;

/// <summary>
/// Profile Batch for Instagram: the profile page opens in a hidden, private web view (no cookies, no sign-in: what any
/// visitor sees), and the video links the page shows are read with InstagramPage.Script.
/// </summary>
static class InstagramWebView
{
    public static async Task<ProfileBatch.Scan> Scan(string url, string user, CancellationToken cancel)
    {
        try { _ = CoreWebView2Environment.GetAvailableBrowserVersionString(); }
        catch (WebView2RuntimeNotFoundException)
        {
            throw new InvalidOperationException("Instagram profiles open in the Microsoft Edge WebView2 Runtime, which is not installed on this computer. "
                + "Install it from Microsoft, or open the profile in your browser, right-click the page and choose \"Download all links with Nexonic\".");
        }
        var data = Path.Combine(Path.GetTempPath(), "nexonic-page-" + Guid.NewGuid().ToString("N"));
        var view = new WebView2();
        var window = new Window
        {
            Width = 1100, Height = 1600, Left = -20000, Top = -20000, WindowStyle = WindowStyle.None,
            ShowInTaskbar = false, ShowActivated = false, Content = view,
        };
        window.Show();
        try
        {
            var env = await CoreWebView2Environment.CreateAsync(null, data);
            var options = env.CreateCoreWebView2ControllerOptions();
            options.IsInPrivateModeEnabled = true;
            await view.EnsureCoreWebView2Async(env, options);
            view.CoreWebView2.Navigate($"https://www.instagram.com/{Uri.EscapeDataString(user)}/");
            var best = (Posts: 0, Videos: new List<BatchEntry>());
            for (int i = 0, same = 0; i < 25 && same < 2; i++)
            {
                await Task.Delay(1000, cancel);
                string json;
                try { json = await view.CoreWebView2.ExecuteScriptAsync(InstagramPage.Script); }
                catch (Exception x) when (x is InvalidOperationException or System.Runtime.InteropServices.COMException) { continue; }
                var now = InstagramPage.Read(json);
                same = now.Posts > 0 && now.Posts == best.Posts && now.Videos.Count == best.Videos.Count ? same + 1 : 0;
                if (now.Posts >= best.Posts) best = now;
            }
            if (best.Posts == 0) throw new InvalidOperationException(InstagramPage.Nothing);
            if (best.Videos.Count == 0) throw new InvalidOperationException(InstagramPage.NoVideos(best.Posts));
            return new ProfileBatch.Scan { Url = url, Owner = user, Entries = best.Videos, Complete = true, Note = InstagramPage.Note(best.Posts) };
        }
        finally
        {
            window.Close();
            view.Dispose();
            try { Directory.Delete(data, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
