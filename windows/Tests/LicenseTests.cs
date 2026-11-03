using System;
using System.IO;
using Xunit;

namespace Nexonic.Tests;

/// The project's own code is offered under MIT OR Apache-2.0; every place that names the license must agree.
public class LicenseTests
{
    static string Root()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "LICENSE-APACHE"))) return d.FullName;
        throw new InvalidOperationException("repository root not found");
    }

    static string Read(string path) => File.ReadAllText(Path.Combine(Root(), path));

    [Fact]
    public void LicenseFilesOfferBoth()
    {
        Assert.Contains("SPDX-License-Identifier: MIT OR Apache-2.0", Read("LICENSE"));
        Assert.StartsWith("MIT License", Read("LICENSE-MIT"));
        Assert.Contains("Copyright (c) 2026 Shahrukh Khalid", Read("LICENSE-MIT"));
        var apache = Read("LICENSE-APACHE");
        Assert.Contains("Version 2.0, January 2004", apache);
        Assert.Contains("END OF TERMS AND CONDITIONS", apache);
    }

    [Theory]
    [InlineData("bucket/nexonic-download-manager.json")]
    [InlineData("packaging/winget/Shahrukhalid.NexonicDownloadManager.locale.en-US.yaml")]
    [InlineData("scripts/package-manifests.py")]
    [InlineData("linux/packaging/build.sh")]
    [InlineData("README.md")]
    [InlineData("TERMS.md")]
    public void EveryPlaceNamesBothLicenses(string path)
    {
        var text = Read(path);
        Assert.True(text.Contains("MIT OR Apache-2.0") || text.Contains("Apache License 2.0"), path);
        Assert.DoesNotContain("\"license\": \"MIT\",", text);
        Assert.DoesNotContain("License: MIT\n", text);
    }
}
