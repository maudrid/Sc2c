# How to try it: the content stage (in progress)

The content stage is being built in the order of `docs/plans/content-stage.md`. This page grows with it.

![The first-run screen](../screenshots/content-first-run.png)

## So far: getting the content

Items 1 and 2 are built. The engine opens the official UQM 0.8.0 content package (`uqm-0.8.0-content.uqm`) in place, refuses any file that isn't the official one (by its SHA-256), and reads files from it by path or by UQM's resource ids. On first start, it asks for the package.

### Try it in the game

To see the first-run screen again, move the game's copy away first: it's `content/uqm-0.8.0-content.uqm` in Godot's user folder (`~/.local/share/godot/app_userdata/Attack on Crateris/` on Linux).

1. Start the game (`godot --path game`). The content screen shows the notice, **Download (11.5 MB)**, **Use a file I already have...** and **Quit**. No "I agree": the license doesn't ask for one.
2. **Download** fetches the package from the UQM project's SourceForge page, with a progress bar, checks it and starts the game. Start again and it goes straight in.
3. Or **Use a file I already have...** and pick a `uqm-0.8.0-content.uqm` you have (from an installed UQM 0.8.0, say). It's checked and copied in; your own file is left alone.
4. A file that isn't the official package is refused with a message saying so, and nothing is kept. If the download fails (no connection, no answer, or nothing arriving for 30 seconds), it says why and you can try again. **Cancel** stops a download or copy part way.
5. In the game, **F1** shows the about screen with the content notice; F1 or Esc closes it.

A headless host or guest (servers, tests) needs the package given: `--content PATH` (the file or its folder) or `CRATERIS_CONTENT`. Without one it stops at once and says so.

### Run the tests

```
dotnet test --filter "ContentPackageTests|ResourceIndexTests|ContentInstallerTests"   # 20 tests
python3 tools/test_network.py
```

`ContentInstallerTests` downloads from a small web server the test runs itself (with redirects, as SourceForge does), and checks a good download is kept unchanged, a wrong one isn't kept and the old copy stays, failures say why (no answer, an error from the server, a dropped connection, nothing arriving for 30 seconds, a folder that can't be written), a download cancelled before or part way leaves nothing behind, a file the player has is checked before it's copied, and a package in use can be replaced. `TheNoticeIsTheOneInTheDesign` keeps the game's notice the same as the one quoted in `docs/design.md`, so a change to the wording goes in the design first.

The test that reads the real package is skipped without it, and says so. To run it, point the tests at the package (the game's own copy is found by itself):

```
CRATERIS_CONTENT=~/Downloads/uqm-0.8.0-content.uqm dotnet test
```

(`CRATERIS_CONTENT` can also name the folder the file is in. If it names a file that doesn't exist, the test fails and says so, rather than being skipped.) What should happen: all tests pass, none skipped. `TheOfficialPackageOpensAndEveryIndexedFileIsInIt` checks the file is the official one and that every one of the 963 entries in its resource index names a file in it.

The network test needs the package too, the same way; it also checks that a headless host without it stops and says how to give it one.

The package is never added to the repository.
