# Attack on Crateris

A free engine for drop-in co-op play of The Ur-Quan Masters content, for up to 4 players. Built in Godot 4 with C#.

Attack on Crateris is not The Ur-Quan Masters and is not made or endorsed by Toys for Bob, Paul Reiche III, Fred Ford or The Ur-Quan Masters project. It is compatible with The Ur-Quan Masters 0.8.0 content, which the game downloads separately on first run.

**Status:** early development. Stages 1 and 2 are built: up to 4 players fly around hyperspace together, joining and leaving at will, and into any star system, around its planets and moons, into orbit to read a planet's report and scan it, all generated exactly as UQM generates them. See `docs/how-to-try/stage-1.md` and `docs/how-to-try/stage-2.md` to try them and `docs/design.md` for the plan.

## License

The engine is free software under the GNU General Public License, version 2 or (at your option) any later version; see `COPYING`. Parts of it are ported from The Ur-Quan Masters source code (also GPL). The Ur-Quan Masters content is licensed separately under CC BY-NC-SA 2.5 and is not included. The project is non-commercial.

## AI-written code

The code in this repository is written by an AI coding assistant (Claude). The project owner designs, reviews and playtests. Every change comes with automated tests and a short "how to try it" note.

## Contributing

Contributions are welcome but optional: forks are fine, and you don't have to send changes back.

## Building

Needs the .NET 8 SDK and the Godot 4.7.2 .NET build. On Linux, `tools/setup-toolchain.sh` installs both (run with sudo).

```
dotnet build game/Crateris.Game.csproj
godot --headless --import --path game
dotnet test                                    # core tests
python3 tools/test_network.py                  # host and guests, end to end
python3 -m unittest discover -s tools/extract  # data checks
godot --path game                              # play (solo host)
```

The game needs the UQM 0.8.0 content, which it downloads on first start (with your click). `python3 tools/test_network.py` uses that copy, or the package named by `CRATERIS_CONTENT`.

## Documents

- `docs/design.md`: design and decisions
- `docs/differences.md`: every deliberate difference from UQM 0.8.0
- `docs/porting-inventory.md`: what is ported from UQM, system by system
- `docs/uqm-data.md`: the UQM data the engine uses and how it is extracted
- `docs/research-lobbies.md`: how other open-source games handle lobbies
- `docs/how-to-try/`: how to try each stage
- `docs/plans/`: plans for upcoming stages
