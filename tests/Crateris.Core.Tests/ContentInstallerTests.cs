// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Crateris.Core.Content;

namespace Crateris.Core.Tests;

/// <summary>
/// Downloading and copying the content package, with the SHA-256 check,
/// against a small web server the test runs itself (no real download).
/// The "package" is bytes made by the test, with their own hash standing
/// in for the official one. The stall tests time real pauses, so they run
/// on their own: alongside the tests that use every core, the test
/// server's thread pool could be starved past the stall limit.
/// </summary>
[Collection(TimingSensitive.Name)]
[Trait("Topic", "content")]
public sealed class ContentInstallerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"crateris-install-{Guid.NewGuid():N}");
    private readonly byte[] _package = RandomNumberGenerator.GetBytes(300_000);
    private readonly string _hash;
    private readonly HttpListener _server = new();
    private readonly string _base;

    public ContentInstallerTests()
    {
        Directory.CreateDirectory(_dir);
        _hash = Convert.ToHexString(SHA256.HashData(_package)).ToLowerInvariant();
        int port = FreePort();
        _base = $"http://127.0.0.1:{port}/";
        _server.Prefixes.Add(_base);
        _server.Start();
        _ = Task.Run(Serve);
    }

    private static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        int port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    // /package: the bytes. /redirect: two redirects, as SourceForge does.
    // /wrong: other bytes. /slow: half, a 3-second pause, then the rest.
    // /cut: half, then the connection is dropped. Anything else: 404.
    private async Task Serve()
    {
        while (_server.IsListening)
        {
            HttpListenerContext ctx;
            try
            {
                ctx = await _server.GetContextAsync();
            }
            catch (Exception)
            {
                return;
            }
            _ = Task.Run(() => Handle(ctx));
        }
    }

    private async Task Handle(HttpListenerContext ctx)
    {
        var res = ctx.Response;
        try
        {
            int half = _package.Length / 2;
            switch (ctx.Request.Url!.AbsolutePath)
            {
                case "/slow":
                    res.ContentLength64 = _package.Length;
                    await res.OutputStream.WriteAsync(_package.AsMemory(0, half));
                    await res.OutputStream.FlushAsync();
                    await Task.Delay(3000);
                    await res.OutputStream.WriteAsync(_package.AsMemory(half));
                    break;
                case "/cut":
                    res.ContentLength64 = _package.Length;
                    await res.OutputStream.WriteAsync(_package.AsMemory(0, half));
                    await res.OutputStream.FlushAsync();
                    res.Abort();
                    return;
                case "/package":
                    res.ContentLength64 = _package.Length;
                    await res.OutputStream.WriteAsync(_package);
                    break;
                case "/redirect":
                    res.Redirect(_base + "mirror");
                    break;
                case "/mirror":
                    res.Redirect(_base + "package");
                    break;
                case "/wrong":
                    var wrong = new byte[1000];
                    res.ContentLength64 = wrong.Length;
                    await res.OutputStream.WriteAsync(wrong);
                    break;
                default:
                    res.StatusCode = 404;
                    break;
            }
            res.Close();
        }
        catch (Exception)
        {
            // The client went away (a cancelled or stalled download).
        }
    }

    public void Dispose()
    {
        _server.Stop();
        _server.Close();
        Directory.Delete(_dir, true);
    }

    private string Destination => ContentLocation.StoredPackage(_dir);

    // An add-on as the installer sees one: served at /package, with its own title.
    private OfficialPackage TestAddon(string path = "redirect") =>
        new("3DO test pack", "uqm-0.8.0-test.uqm", _package.Length, _hash, _base + path);

    [Fact]
    public void TheAddonsAreTheProjectsOfficialFiles()
    {
        Assert.Equal(new[] { OfficialPackages.Voice, OfficialPackages.Music }, OfficialPackages.Addons);
        foreach (var p in OfficialPackages.Addons.Append(OfficialPackages.Content))
        {
            Assert.Matches("^[0-9a-f]{64}$", p.Sha256);
            Assert.Equal($"https://sourceforge.net/projects/sc2/files/UQM/0.8/{p.FileName}/download", p.DownloadUrl);
        }
        Assert.Equal(("uqm-0.8.0-voice.uqm", 115_164_704L), (OfficialPackages.Voice.FileName, OfficialPackages.Voice.Size));
        Assert.Equal(("uqm-0.8.0-3domusic.uqm", 18_980_671L), (OfficialPackages.Music.FileName, OfficialPackages.Music.Size));
        Assert.Equal("115.2 MB", OfficialPackages.Voice.SizeText);
        Assert.Equal(ContentPackage.OfficialSha256, OfficialPackages.Content.Sha256);
    }

    [Fact]
    public async Task AnAddonIsDownloadedBesideTheContentAndCountsAsHereAtItsSize()
    {
        var addon = TestAddon();
        string where = OfficialPackages.StoredPath(_dir, addon);
        Assert.Equal(Path.Combine(_dir, ContentLocation.UserSubfolder, "uqm-0.8.0-test.uqm"), where);
        Assert.False(OfficialPackages.IsInstalled(_dir, addon));
        using var http = new HttpClient();
        await ContentInstaller.Download(http, addon, where);
        Assert.Equal(_package, File.ReadAllBytes(where));
        Assert.True(OfficialPackages.IsInstalled(_dir, addon));
        // Cut short, it isn't counted.
        File.WriteAllBytes(where, _package[..1000]);
        Assert.False(OfficialPackages.IsInstalled(_dir, addon));
    }

    // A step for the package served at path, kept as name.
    private DownloadStep Step(string path, string name, bool required = false) =>
        new(new OfficialPackage(name, name + ".uqm", _package.Length, _hash, _base + path),
            Path.Combine(_dir, ContentLocation.UserSubfolder, name + ".uqm"), required);

    [Fact]
    public async Task ARunFetchesEachInTurnAndAFailedAddonDoesntStopTheRest()
    {
        using var http = new HttpClient();
        var steps = new[] { Step("package", "content", required: true), Step("wrong", "voice"), Step("redirect", "music") };
        var started = new List<string>();
        var r = await PackageDownloads.Run(http, steps, s => started.Add(s.Package.Title));
        Assert.Equal(DownloadOutcome.OptionalFailed, r.Outcome);
        Assert.Equal(new[] { "content", "voice", "music" }, started);
        Assert.Equal(new[] { "content", "music" }, r.Installed.Select(p => p.Title));
        Assert.Single(r.Problems);
        Assert.Contains("isn't the official UQM 0.8.0 voice", r.Problems[0]);
        Assert.False(File.Exists(steps[1].Destination));

        var done = await PackageDownloads.Run(http, new[] { Step("package", "again") });
        Assert.Equal((DownloadOutcome.Done, 0), (done.Outcome, done.Problems.Count));
    }

    [Fact]
    public async Task ARequiredPackageThatFailsStopsTheRun()
    {
        using var http = new HttpClient();
        var steps = new[] { Step("wrong", "content", required: true), Step("package", "music") };
        var r = await PackageDownloads.Run(http, steps);
        Assert.Equal(DownloadOutcome.RequiredFailed, r.Outcome);
        Assert.Empty(r.Installed);
        Assert.False(File.Exists(steps[1].Destination));
    }

    [Theory]
    [InlineData(0)]   // cancelled during the content: no add-on is started
    [InlineData(1)]   // cancelled during an add-on: the next isn't started
    public async Task CancellingStopsTheWholeRun(int during)
    {
        using var http = new HttpClient();
        var steps = new[] { Step(during == 0 ? "slow" : "package", "content", required: true),
            Step(during == 1 ? "slow" : "package", "voice"), Step("package", "music") };
        using var cancel = new CancellationTokenSource();
        var started = new List<string>();
        var r = await PackageDownloads.Run(http, steps, s =>
        {
            started.Add(s.Package.Title);
            if (s == steps[during])
                cancel.CancelAfter(300);   // part way through its slow download
        }, cancel: cancel.Token);
        Assert.Equal(DownloadOutcome.Cancelled, r.Outcome);
        Assert.Equal(steps.Take(during + 1).Select(s => s.Package.Title), started);
        Assert.Equal(steps.Take(during).Select(s => s.Package.Title), r.Installed.Select(p => p.Title));
        Assert.False(File.Exists(steps[2].Destination));
    }

    [Fact]
    public void TheContentIsKeptWhereItAlwaysWas()
    {
        Assert.Equal(ContentLocation.StoredPackage(_dir), OfficialPackages.StoredPath(_dir, OfficialPackages.Content));
    }

    /// <summary>
    /// The real add-ons, if CRATERIS_ADDONS names a folder holding them
    /// (they're never in the repository): each is the pinned file.
    /// </summary>
    [Fact]
    public void TheRealAddonsMatchTheirPinnedHashes()
    {
        string? folder = Environment.GetEnvironmentVariable("CRATERIS_ADDONS");
        if (folder is null)
            return;   // nothing to check without them (a plain Fact: no content is needed otherwise)
        foreach (var p in OfficialPackages.Addons)
        {
            string file = Path.Combine(folder, p.FileName);
            if (!File.Exists(file))
                continue;
            Assert.Equal(p.Size, new FileInfo(file).Length);
            Assert.Equal(p.Sha256, ContentPackage.Sha256Of(file));
        }
    }

    [Fact]
    public async Task AWrongAddonIsRefusedByItsOwnName()
    {
        var addon = TestAddon("wrong");
        string where = OfficialPackages.StoredPath(_dir, addon);
        using var http = new HttpClient();
        var e = await Assert.ThrowsAsync<ContentPackageException>(() => ContentInstaller.Download(http, addon, where));
        Assert.Contains("isn't the official UQM 0.8.0 3DO test pack", e.Message);
        Assert.False(File.Exists(where));
        // A file the player has: checked the same way.
        string mine = Path.Combine(_dir, "mine.uqm");
        File.WriteAllBytes(mine, new byte[10]);
        e = await Assert.ThrowsAsync<ContentPackageException>(() => ContentInstaller.CopyFrom(mine, addon, where));
        Assert.Contains("3DO test pack", e.Message);
        File.WriteAllBytes(mine, _package);
        await ContentInstaller.CopyFrom(mine, addon, where);
        Assert.Equal(_package, File.ReadAllBytes(where));
    }

    [Fact]
    public async Task ADownloadFollowsRedirectsAndIsKeptUnchanged()
    {
        using var http = new HttpClient();
        var reports = new List<InstallProgress>();
        await ContentInstaller.Download(http, _base + "redirect", Destination,
            new SyncProgress(reports.Add), expectedSha256: _hash);
        Assert.Equal(_package, File.ReadAllBytes(Destination));
        Assert.False(File.Exists(Destination + ContentInstaller.PartialSuffix));
        Assert.Equal(new InstallProgress(_package.Length, _package.Length), reports[^1]);
        Assert.Equal(0, reports[0].Done);
    }

    [Fact]
    public async Task AWrongDownloadIsNotKeptAndTheOldCopyStays()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Destination)!);
        File.WriteAllBytes(Destination, _package);   // a good copy already there
        using var http = new HttpClient();
        var e = await Assert.ThrowsAsync<ContentPackageException>(() =>
            ContentInstaller.Download(http, _base + "wrong", Destination, expectedSha256: _hash));
        Assert.Contains("isn't the official UQM 0.8.0 content", e.Message);
        Assert.Contains("not kept", e.Message);
        Assert.Equal(_package, File.ReadAllBytes(Destination));
        Assert.False(File.Exists(Destination + ContentInstaller.PartialSuffix));
    }

    [Fact]
    public async Task AFailedDownloadSaysWhy()
    {
        using var http = new HttpClient();
        var e = await Assert.ThrowsAsync<ContentPackageException>(() =>
            ContentInstaller.Download(http, _base + "missing", Destination, expectedSha256: _hash));
        Assert.Contains("404", e.Message);
        Assert.False(File.Exists(Destination));

        var unreachable = await Assert.ThrowsAsync<ContentPackageException>(() =>
            ContentInstaller.Download(http, $"http://127.0.0.1:{FreePort()}/package", Destination, expectedSha256: _hash));
        Assert.Contains("Couldn't reach the download", unreachable.Message);
    }

    [Fact]
    public async Task ACancelledDownloadLeavesNothingBehind()
    {
        using var http = new HttpClient();
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ContentInstaller.Download(http, _base + "package", Destination, cancel: cancel.Token, expectedSha256: _hash));
        Assert.False(File.Exists(Destination));
        Assert.False(File.Exists(Destination + ContentInstaller.PartialSuffix));
    }

    [Fact]
    public async Task CancellingPartWayLeavesNothingBehind()
    {
        using var http = new HttpClient();
        using var cancel = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ContentInstaller.Download(http, _base + "slow", Destination,
                new SyncProgress(p => { if (p.Done > 0) cancel.Cancel(); }), cancel.Token, _hash));
        Assert.False(File.Exists(Destination));
        Assert.False(File.Exists(Destination + ContentInstaller.PartialSuffix));
    }

    [Fact]
    public async Task ADroppedConnectionSaysSoAndLeavesNothingBehind()
    {
        using var http = new HttpClient();
        var e = await Assert.ThrowsAsync<ContentPackageException>(() =>
            ContentInstaller.Download(http, _base + "cut", Destination, expectedSha256: _hash));
        Assert.Contains("couldn't be saved", e.Message);
        Assert.False(File.Exists(Destination));
        Assert.False(File.Exists(Destination + ContentInstaller.PartialSuffix));
    }

    [Fact]
    public async Task AStalledDownloadStopsAndSaysSo()
    {
        using var http = new HttpClient();
        var e = await Assert.ThrowsAsync<ContentPackageException>(() =>
            ContentInstaller.Download(http, _base + "slow", Destination, expectedSha256: _hash,
                stallTimeout: TimeSpan.FromSeconds(0.5)));
        Assert.Contains("nothing arrived", e.Message);
        Assert.False(File.Exists(Destination + ContentInstaller.PartialSuffix));

        // A slow but moving download is fine.
        await ContentInstaller.Download(http, _base + "slow", Destination, expectedSha256: _hash,
            stallTimeout: TimeSpan.FromSeconds(10));
        Assert.Equal(_package, File.ReadAllBytes(Destination));
    }

    [Fact]
    public async Task ADownloadThatDoesntAnswerSaysSo()
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(0.5) };
        // Headers come at once for /slow; a server that never answers is
        // simulated by a listener that accepts nothing.
        var silent = new TcpListener(IPAddress.Loopback, 0);
        silent.Start();
        try
        {
            int port = ((IPEndPoint)silent.LocalEndpoint).Port;
            var e = await Assert.ThrowsAsync<ContentPackageException>(() =>
                ContentInstaller.Download(http, $"http://127.0.0.1:{port}/package", Destination, expectedSha256: _hash));
            Assert.Contains("didn't answer", e.Message);
        }
        finally
        {
            silent.Stop();
        }
    }

    [Fact]
    public async Task AFolderThatCantBeWrittenSaysSo()
    {
        if (OperatingSystem.IsWindows() || Environment.UserName == "root")
            return;   // root can write anywhere; Windows has no chmod
        string locked = Path.Combine(_dir, "locked");
        Directory.CreateDirectory(locked);
        File.SetUnixFileMode(locked, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        try
        {
            using var http = new HttpClient();
            var e = await Assert.ThrowsAsync<ContentPackageException>(() =>
                ContentInstaller.Download(http, _base + "package", Path.Combine(locked, ContentPackage.FileName), expectedSha256: _hash));
            Assert.Contains("couldn't be saved", e.Message);
        }
        finally
        {
            File.SetUnixFileMode(locked, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    [Fact]
    public void TheNoticeIsTheOneInTheDesign()
    {
        // docs/design.md quotes the notice ("First-run content download");
        // the game must show exactly that text.
        string dir = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(dir, "docs", "design.md")))
            dir = Path.GetDirectoryName(dir) ?? throw new InvalidOperationException("no docs/design.md above the tests");
        var lines = File.ReadAllLines(Path.Combine(dir, "docs", "design.md"));
        int start = Array.FindIndex(lines, l => l == $"> **{ContentNotice.Title}**");
        Assert.True(start >= 0, "the design's notice title isn't the game's");
        var paragraphs = lines.Skip(start + 1)
            .TakeWhile(l => l.StartsWith('>') && !l.StartsWith("> [ ]") && !l.StartsWith("> **Download"))
            .Select(l => l.TrimStart('>').Trim())
            .Where(l => l.Length > 0)
            .ToList();
        Assert.Equal(paragraphs, ContentNotice.Paragraphs);
    }

    [Fact]
    public async Task AFileThePlayerHasIsCheckedThenCopied()
    {
        string mine = Path.Combine(_dir, "my-uqm", ContentPackage.FileName);
        Directory.CreateDirectory(Path.GetDirectoryName(mine)!);
        File.WriteAllBytes(mine, _package);
        await ContentInstaller.CopyFrom(mine, Destination, expectedSha256: _hash);
        Assert.Equal(_package, File.ReadAllBytes(Destination));
        Assert.True(File.Exists(mine));   // the player's own file is left as it was

        string other = Path.Combine(_dir, "other.uqm");
        File.WriteAllBytes(other, new byte[10]);
        var e = await Assert.ThrowsAsync<ContentPackageException>(() => ContentInstaller.CopyFrom(other, Destination, expectedSha256: _hash));
        Assert.Contains("isn't the official", e.Message);
        Assert.Equal(_package, File.ReadAllBytes(Destination));

        // Choosing the stored copy itself only checks it.
        await ContentInstaller.CopyFrom(Destination, Destination, expectedSha256: _hash);
        Assert.Equal(_package, File.ReadAllBytes(Destination));

        await Assert.ThrowsAsync<ContentPackageException>(() => ContentInstaller.CopyFrom(Path.Combine(_dir, "nope.uqm"), Destination));
    }

    [Fact]
    public async Task APackageInUseCanBeReplaced()
    {
        // The game may hold the package open (ContentPackage) while a new
        // copy is put in place.
        Directory.CreateDirectory(Path.GetDirectoryName(Destination)!);
        File.WriteAllBytes(Destination, new byte[5]);
        using (new FileStream(Destination, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
        {
            using var http = new HttpClient();
            await ContentInstaller.Download(http, _base + "package", Destination, expectedSha256: _hash);
        }
        Assert.Equal(_package, File.ReadAllBytes(Destination));
    }

    // Progress<T> posts to the thread pool; this one reports at once, in order.
    private sealed class SyncProgress : IProgress<InstallProgress>
    {
        private readonly Action<InstallProgress> _report;
        public SyncProgress(Action<InstallProgress> report) => _report = report;
        public void Report(InstallProgress value) => _report(value);
    }
}

/// <summary>Tests that time real pauses: run on their own, not alongside the others.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class TimingSensitive
{
    public const string Name = "Timing-sensitive";
}
