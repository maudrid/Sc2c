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
/// in for the official one.
/// </summary>
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
    // /wrong: other bytes. Anything else: 404.
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
            var res = ctx.Response;
            switch (ctx.Request.Url!.AbsolutePath)
            {
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
    }

    public void Dispose()
    {
        _server.Stop();
        _server.Close();
        Directory.Delete(_dir, true);
    }

    private string Destination => ContentLocation.StoredPackage(_dir);

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
