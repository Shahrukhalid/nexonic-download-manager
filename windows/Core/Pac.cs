using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Nexonic.Core;

/// <summary>
/// Automatic proxy configuration (Options → Connection → Proxy → "Automatic configuration script"): a PAC file from a web
/// address or a local file decides the proxy for each address. The script runs in the bundled JavaScript engine with no file,
/// network or program access (it cannot import modules or build code from text) and is stopped after a few seconds.
/// </summary>
public static class Pac
{
    static readonly ConcurrentDictionary<string, (string Script, DateTime At)> scripts = new();
    static readonly ConcurrentDictionary<string, (string Result, DateTime At)> answers = new();
    static readonly TimeSpan Keep = TimeSpan.FromMinutes(10);
    const int MaxScript = 1024 * 1024;

    /// <summary>Whether <paramref name="address"/> can be a script location: an http(s) or file address, or a full path.</summary>
    public static bool AddressOk(string? address)
    {
        var a = (address ?? "").Trim();
        if (a.Length == 0) return false;
        if (Uri.TryCreate(a, UriKind.Absolute, out var u) && u.Scheme is "http" or "https" or "file") return true;
        return Path.IsPathFullyQualified(a);
    }

    /// <summary>The proxy for <paramref name="url"/>: null = connect directly. Errors (no script, a broken script) also mean directly.</summary>
    public static Uri? ProxyFor(string address, Uri url) => First(Answer(address, url));

    /// <summary>The script's answer for one address ("PROXY host:port; DIRECT"), cached per host for ten minutes.</summary>
    public static string Answer(string address, Uri url)
    {
        var key = address + "\n" + url.Scheme + "://" + url.Host;
        if (answers.TryGetValue(key, out var hit) && DateTime.UtcNow - hit.At < Keep) return hit.Result;
        string result;
        try { result = Run(Load(address), url) ?? "DIRECT"; }
        catch { result = "DIRECT"; }
        answers[key] = (result, DateTime.UtcNow);
        return result;
    }

    /// <summary>Forgets loaded scripts and answers (after the settings change).</summary>
    public static void Reset() { scripts.Clear(); answers.Clear(); }

    /// <summary>The first usable entry of an answer: null for DIRECT (or nothing usable).</summary>
    public static Uri? First(string? answer)
    {
        foreach (var part in (answer ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var bits = part.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var kind = bits[0].ToUpperInvariant();
            if (kind == "DIRECT") return null;
            if (bits.Length < 2) continue;
            var scheme = kind switch { "PROXY" or "HTTP" or "HTTPS" => "http", "SOCKS" or "SOCKS5" => "socks5", "SOCKS4" => "socks4", _ => null };
            if (scheme != null && Uri.TryCreate($"{scheme}://{bits[1]}", UriKind.Absolute, out var u) && u.Port is > 0 and < 65536) return u;
        }
        return null;
    }

    /// <summary>Reads the script (at most 1 MB; web addresses are fetched directly, never through a proxy).</summary>
    public static string Load(string address)
    {
        address = address.Trim();
        if (scripts.TryGetValue(address, out var hit) && DateTime.UtcNow - hit.At < Keep) return hit.Script;
        string text;
        if (Uri.TryCreate(address, UriKind.Absolute, out var u) && u.Scheme is "http" or "https")
        {
            using var http = new HttpClient(new SocketsHttpHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(15) };
            var bytes = http.GetByteArrayAsync(u).GetAwaiter().GetResult();
            if (bytes.Length > MaxScript) throw new InvalidDataException("The proxy script is larger than 1 MB.");
            text = Encoding.UTF8.GetString(bytes);
        }
        else
        {
            var path = u?.IsFile == true ? u.LocalPath : address;
            if (new FileInfo(path).Length > MaxScript) throw new InvalidDataException("The proxy script is larger than 1 MB.");
            text = File.ReadAllText(path);
        }
        scripts[address] = (text, DateTime.UtcNow);
        return text;
    }

    /// <summary>Scripts may not load modules: proxy scripts never need it, and a module could reach files and programs.</summary>
    public static bool Allowed(string script) => !Regex.IsMatch(script, @"\bimport\b");

    /// <summary>Runs FindProxyForURL(url, host) and returns its answer (null when the engine is missing or the script fails).</summary>
    public static string? Run(string script, Uri url, string? engine = null, int timeoutMs = 5000)
    {
        engine ??= SiteVideoTools.QuickJs;
        if (engine == null || !Allowed(script)) return null;
        var dns = new Dictionary<string, string>();
        foreach (var host in new[] { url.Host, Dns.GetHostName() })
            try
            {
                var a = IPAddress.TryParse(host, out var ip) ? ip : Dns.GetHostAddresses(host).FirstOrDefault(x => x.AddressFamily == AddressFamily.InterNetwork);
                if (a != null) dns[host] = a.ToString();
            }
            catch { /* not resolvable: dnsResolve answers null */ }
        var me = dns.GetValueOrDefault(Dns.GetHostName()) ?? "127.0.0.1";
        var file = Path.Combine(Path.GetTempPath(), $"nexonic-pac-{Guid.NewGuid():N}.js");
        File.WriteAllText(file, Prelude + "\nvar __dns=" + JsonSerializer.Serialize(dns) + ", __me=" + JsonSerializer.Serialize(me) + ";\n" +
            script + "\n;console.log('\\u0001' + String(FindProxyForURL(" + JsonSerializer.Serialize(url.AbsoluteUri) + ", " + JsonSerializer.Serialize(url.Host) + ")));\n");
        try
        {
            var psi = new ProcessStartInfo(engine) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
            foreach (var a in new[] { "--script", "--memory-limit", "65536", "--stack-size", "1024", file }) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi)!;
            var output = p.StandardOutput.ReadToEndAsync();
            _ = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(timeoutMs)) { try { p.Kill(true); } catch { } return null; }
            var line = output.Result.Split('\n').LastOrDefault(l => l.StartsWith('\u0001'));
            return line?[1..].Trim();
        }
        finally { try { File.Delete(file); } catch { } }
    }

    /// <summary>The standard PAC helper functions; code can no longer be built from text once they are defined.</summary>
    const string Prelude = """
        "use strict";
        (function(){var g=globalThis;delete g.eval;g.Function=undefined;
          for(const f of [function(){},async function(){},function*(){},async function*(){}])
            Object.defineProperty(Object.getPrototypeOf(f),'constructor',{value:undefined});})();
        function dnsResolve(h){return Object.prototype.hasOwnProperty.call(__dns,h)?__dns[h]:(/^\d+\.\d+\.\d+\.\d+$/.test(h)?h:null);}
        function myIpAddress(){return __me;}
        function isPlainHostName(h){return h.indexOf('.')<0;}
        function dnsDomainIs(h,d){h=String(h).toLowerCase();d=String(d).toLowerCase();return h.length>=d.length&&h.substring(h.length-d.length)===d;}
        function localHostOrDomainIs(h,hd){return h===hd||(h.indexOf('.')<0&&hd.lastIndexOf(h+'.',0)===0);}
        function isResolvable(h){return dnsResolve(h)!==null;}
        function dnsDomainLevels(h){return h.split('.').length-1;}
        function __n(ip){var p=String(ip).split('.');if(p.length!==4)return null;var n=0;for(var i=0;i<4;i++){var v=+p[i];if(!(v>=0&&v<=255))return null;n=n*256+v;}return n;}
        function isInNet(h,pat,mask){var a=__n(dnsResolve(h)),b=__n(pat),m=__n(mask);if(a===null||b===null||m===null)return false;
          for(var i=3;i>=0;i--){var d=Math.pow(256,i);if((Math.floor(a/d)%256&Math.floor(m/d)%256)!==(Math.floor(b/d)%256&Math.floor(m/d)%256))return false;}return true;}
        function shExpMatch(s,p){var r='^'+String(p).replace(/[.+^${}()|[\]\\]/g,'\\$&').replace(/\*/g,'.*').replace(/\?/g,'.')+'$';return new RegExp(r).test(s);}
        function convert_addr(ip){return __n(ip)||0;}
        var __days=['SUN','MON','TUE','WED','THU','FRI','SAT'],__months=['JAN','FEB','MAR','APR','MAY','JUN','JUL','AUG','SEP','OCT','NOV','DEC'];
        function __gmt(a){return a.length>0&&a[a.length-1]==='GMT';}
        function __now(a){var d=new Date();return __gmt(a)?{wd:d.getUTCDay(),D:d.getUTCDate(),M:d.getUTCMonth(),Y:d.getUTCFullYear(),h:d.getUTCHours(),m:d.getUTCMinutes(),s:d.getUTCSeconds()}
          :{wd:d.getDay(),D:d.getDate(),M:d.getMonth(),Y:d.getFullYear(),h:d.getHours(),m:d.getMinutes(),s:d.getSeconds()};}
        function __in(v,a,b){return a<=b?(v>=a&&v<=b):(v>=a||v<=b);}
        function weekdayRange(){var a=Array.prototype.slice.call(arguments),t=__now(a);if(__gmt(a))a.pop();var x=__days.indexOf(a[0]),y=a.length>1?__days.indexOf(a[1]):x;return __in(t.wd,x,y);}
        function dateRange(){var a=Array.prototype.slice.call(arguments),t=__now(a);if(__gmt(a))a.pop();
          function v(x){if(typeof x==='string')return {M:__months.indexOf(x.toUpperCase())};return x>31?{Y:x}:{D:x};}
          var p=a.map(v);if(p.length===1)p.push(p[0]);var h=p.length/2,s={},e={};
          for(var i=0;i<h;i++){Object.assign(s,p[i]);Object.assign(e,p[i+h]);}
          if(s.Y===undefined&&s.M===undefined)return __in(t.D,s.D,e.D);
          if(s.Y===undefined&&s.D===undefined)return __in(t.M,s.M,e.M);
          if(s.M===undefined&&s.D===undefined)return __in(t.Y,s.Y,e.Y);
          var ks=((s.Y!==undefined?s.Y:t.Y)*12+(s.M!==undefined?s.M:0))*32+(s.D!==undefined?s.D:1),ke=((e.Y!==undefined?e.Y:t.Y)*12+(e.M!==undefined?e.M:11))*32+(e.D!==undefined?e.D:31);
          var kn=(t.Y*12+t.M)*32+t.D;return s.Y===undefined?__in(kn,ks,ke):(kn>=ks&&kn<=ke);}
        function timeRange(){var a=Array.prototype.slice.call(arguments),t=__now(a);if(__gmt(a))a.pop();var n=t.h*3600+t.m*60+t.s;
          if(a.length===1)return t.h===a[0];if(a.length===2)return __in(t.h,a[0],a[1]-1)||(a[0]===a[1]&&t.h===a[0]);
          if(a.length===4)return __in(n,a[0]*3600+a[1]*60,a[2]*3600+a[3]*60);if(a.length===6)return __in(n,a[0]*3600+a[1]*60+a[2],a[3]*3600+a[4]*60+a[5]);return false;}
        function isResolvableEx(h){return isResolvable(h);}function dnsResolveEx(h){return dnsResolve(h)||'';}function myIpAddressEx(){return myIpAddress();}
        function isInNetEx(h,p){var s=p.split('/');var b=+s[1],m=[];for(var i=0;i<4;i++){var k=Math.max(0,Math.min(8,b-i*8));m.push(256-Math.pow(2,8-k));}return isInNet(h,s[0],m.join('.'));}
        """;
}

/// <summary>A proxy decided per address by a PAC script (see <see cref="Pac"/>).</summary>
public sealed class PacProxy(string address, ICredentials? credentials) : IWebProxy
{
    public ICredentials? Credentials { get; set; } = credentials;
    public Uri? GetProxy(Uri destination) => Pac.ProxyFor(address, destination);
    public bool IsBypassed(Uri host) => GetProxy(host) == null;
}
