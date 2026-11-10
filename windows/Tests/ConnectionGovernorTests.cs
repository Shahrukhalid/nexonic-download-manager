using Nexonic.Core;
using Xunit;

/// <summary>Automatic connections: grow while it helps, stop when it does not, back off when the server refuses.</summary>
public class ConnectionGovernorTests
{
    static readonly DateTime T0 = new(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void AutoGrowsWhileFasterAndStopsWhenNot()
    {
        var g = new ConnectionGovernor(auto: true, maximum: 256, T0);
        Assert.Equal(ConnectionGovernor.AutoStart, g.Allowed);
        var t = T0;
        double speed = 1_000_000;
        for (var i = 0; i < 6; i++)  // speed rises with connections: keep growing
        {
            t += ConnectionGovernor.Sample;
            g.Received((long)(speed * ConnectionGovernor.Sample.TotalSeconds));
            g.Tick(t, g.Allowed);
            speed *= 1.5;
        }
        speed /= 1.5;  // the last measured speed, now flat
        var grown = g.Allowed;
        Assert.True(grown > ConnectionGovernor.AutoStart);
        for (var i = 0; i < 5; i++)  // no more gain: the number stays
        {
            t += ConnectionGovernor.Sample;
            g.Received((long)(speed * ConnectionGovernor.Sample.TotalSeconds));
            g.Tick(t, g.Allowed);
        }
        Assert.Equal(grown, g.Allowed);
        Assert.True(g.Allowed <= 256);
    }

    [Fact]
    public void RefusalsHalveAndWaitAsAsked()
    {
        var g = new ConnectionGovernor(auto: false, maximum: 64, T0);
        Assert.Equal(64, g.Allowed);
        g.Refused(T0, TimeSpan.FromSeconds(20));
        Assert.Equal(32, g.Allowed);
        Assert.Equal(T0.AddSeconds(20), g.QuietUntil);
        g.Tick(T0.AddSeconds(10), 32);
        Assert.Equal(32, g.Allowed);  // still quiet
        g.Tick(T0.AddSeconds(30), 32);
        Assert.True(g.Allowed > 32);  // a fixed number slowly comes back
        g.Refused(T0, TimeSpan.FromDays(1));
        Assert.Equal(T0.AddMinutes(10), g.QuietUntil);  // never waits longer than 10 minutes
        for (var i = 0; i < 10; i++) g.Refused(T0, null);
        Assert.Equal(1, g.Allowed);  // one connection always stays
    }

    [Fact]
    public void AutoNeverStartsAboveItsMaximum()
    {
        Assert.Equal(2, new ConnectionGovernor(true, 2, T0).Allowed);
        Assert.Equal(256, new ConnectionGovernor(false, 999, T0).Allowed);
        Assert.Equal(1, new ConnectionGovernor(false, 0, T0).Allowed);
    }

    [Fact]
    public void ReadsRetryAfterAndRefusalCodes()
    {
        Assert.Equal(TimeSpan.FromSeconds(7), ConnectionGovernor.RetryAfter(new(TimeSpan.FromSeconds(7)), T0));
        Assert.Equal(TimeSpan.FromSeconds(90), ConnectionGovernor.RetryAfter(new(new DateTimeOffset(T0.AddSeconds(90))), T0));
        Assert.Null(ConnectionGovernor.RetryAfter(null, T0));
        Assert.True(ConnectionGovernor.IsRefusal(System.Net.HttpStatusCode.TooManyRequests));
        Assert.True(ConnectionGovernor.IsRefusal(System.Net.HttpStatusCode.ServiceUnavailable));
        Assert.False(ConnectionGovernor.IsRefusal(System.Net.HttpStatusCode.NotFound));
    }
}
