# How to try it: the content stage (in progress)

The content stage is being built in the order of `docs/plans/content-stage.md`. This page grows with it.

## So far: reading the content package

Item 1 is built: the engine can open the official UQM 0.8.0 content package (`uqm-0.8.0-content.uqm`) in place, refuses any file that isn't the official one (by its SHA-256), and reads files from it by path or by UQM's resource ids. The download screen comes with item 2; until then, get the package by hand to run the tests that need it.

```
dotnet test --filter "ContentPackageTests|ResourceIndexTests"   # 6 tests
```

Without the package, the test that needs it is skipped and says so. To run it, download `uqm-0.8.0-content.uqm` from the UQM project (https://sourceforge.net/projects/sc2/files/UQM/0.8/) and point the tests at it:

```
CRATERIS_CONTENT=~/Downloads/uqm-0.8.0-content.uqm dotnet test
```

(`CRATERIS_CONTENT` can also name the folder the file is in.) What should happen: all tests pass, none skipped. `TheOfficialPackageOpensAndEveryIndexedFileIsInIt` checks the file is the official one and that every one of the 963 entries in its resource index names a file in it.

The package is never added to the repository.
