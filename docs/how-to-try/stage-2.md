# How to try it: stage 2, solar systems and planets (in progress)

Stage 2 is being built in the order of `docs/plans/stage-2.md`. This page grows with it.

## So far: every star system, generated as UQM does it

Items 1 to 3 are built: the system random number generator, and the generation of every system's planets, moons and planet data, including the special systems (Sol, Spathiwa, the Chmmr, the Sa-Matra and the rest). There is nothing new on screen yet; the system views come with item 10.

Set up and build as for stage 1 (`docs/how-to-try/stage-1.md`), then:

```
dotnet test --filter StarSystemTests   # generation against UQM: 3 tests
dotnet test                            # everything: 29 tests
```

`EveryStarSystemMatchesUqm` generates all 502 systems and compares every planet and moon (type, orbit, position, seed, number of moons) and every world's planet data (temperature, gravity, tectonics, weather, atmosphere, rotation, tilt and so on) with what UQM's own code produces. If anything differs, the failure lists the star, the world and the value.

To record the reference again from the UQM source (needs gcc and a checkout of UQM 0.8.0):

```
python3 tools/extract/golden_uqm.py --uqm ../uqm-0.8.0/sc2
git diff --stat tests/golden   # should show no changes
```
