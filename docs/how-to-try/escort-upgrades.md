# How to try it: escorts' upgrades

A guest's escort can now buy crew pods, fuel tanks, fusion thrusters and turning jets at the starbase, as the flagship can, each up to a cap the host sets below the flagship: 8 thrusters, 6 turning jets, 4 fuel tanks and 2 crew pods for now (placeholders, to be tuned in playtesting), the escort's kit's own counted. They cost what the flagship's cost, sell back as the flagship's do (the kit's too), and do what the flagship's do: 50 crew a crew pod, 50 units of fuel a tank, and more thrust and quicker turning in flight and in battle. The flagship keeps only the high-efficiency fuel system and the bomb's parts. The plan, with the readings it chose, is `docs/plans/escort-upgrades.md`; what differs from UQM is in `docs/differences.md` (row 9).

## Set up

As for the content stage (`docs/how-to-try/content-stage.md`):

```
dotnet build game/Crateris.Game.csproj
godot --headless --import --path game
```

## Run the tests

```
CRATERIS_CONTENT=~/Downloads/uqm-0.8.0-content.uqm python3 tools/run_tests.py   # every topic
CRATERIS_CONTENT=~/Downloads/uqm-0.8.0-content.uqm python3 tools/run_tests.py starbase
```

What should happen: every topic passes and none is skipped. The new tests are `EscortUpgradeTests` in the topic `starbase`.

## Try it

```
godot --path game -- --host --name Host --start-docked --start-units 5000 --save /tmp/escorts/save.json
godot --path game -- --join 127.0.0.1 --name Guest --id guest-1
```

The host gives the guest some resource units (T opens the giving screen; Page Up gives 100 more; Enter gives them). The guest flies into the starbase by Earth (left from where it undocks, a touch of thrust) and docks, then Outfit Starship, Modules:

1. **Thrusters and jets.** Up to the drive row (the top), or the jets under it: Enter on an empty slot offers the fusion thruster (or the turning jets), as the flagship's screen does, and Enter buys it for 500. The escort's kit has two of each; it may hold 8 thrusters and 6 jets.
2. **Crew pods and fuel tanks.** In the module row Enter on an empty slot opens the choice on the crew pod (2,000), then the storage bay, the fuel tank (500) and the combat modules; never the high-efficiency fuel system. The kit has one fuel tank; it may hold 4 tanks and 2 crew pods.
3. **The caps.** Past a cap the choice still opens, and buying is refused as a module too dear is: "That can't be done."
4. **Selling.** Enter on a full slot sells it at its price, the kit's thrusters, jets and fuel tank too, as the flagship's; a crew pod the crew needs and a fuel tank the fuel needs are refused, as UQM refuses them.
5. **What they do.** A crew pod makes room for 50 more crew, hired at the shipyard's Crew at the group's crew price (the kit holds 10: with both pods 110). A tank makes room for 50 more units of fuel. More thrusters and jets fly faster and turn quicker in hyperspace, in systems and in battle, where the escort is the flagship's ship with its own slots.

The caps are host settings, saved with the game (`settings.escort_caps` in the save: `thrusters`, `jets`, `fuel_tanks`, `crew_pods`); there is no option for them yet. A save from before them has the placeholders. To change one, edit it in the save and load it; each is from 0 to one less than the flagship's slots for it (at most 10 thrusters, 7 jets, 15 fuel tanks, 15 crew pods: below the flagship), and a save with one outside that isn't loaded.

## What was run

Each run this way, headless, with `--save` and `--report` files in a scratch folder (`/tmp/escorts` here).

- The host and a guest, the host giving 5,000 resource units and the guest buying:

  ```
  GIVE=$(python3 -c "print(','.join(['pageup:1,none:2'] * 50))")
  godot --headless --path game -- --host --name Host --start-docked --start-units 5000 --save /tmp/escorts/save.json --report /tmp/escorts/host.json --script "none:200,give:1,none:5,$GIVE,select:1,none:3,give:1,none:800" --quit-after 1300
  godot --headless --path game -- --join 127.0.0.1 --name Guest --id guest-1 --report /tmp/escorts/guest.json --script "none:80,left:8,thrust:4,none:640,down:1,none:3,select:1,none:6,down:1,none:3,select:1,none:6,select:1,none:6,select:1,none:6,menuright:1,none:3,menuright:1,none:3,select:1,none:6,select:1,none:6,menuright:1,none:3,select:1,none:6,select:1,none:6,cancel:1,none:6,up:1,none:3,menuright:1,none:3,select:1,none:6,select:1,none:6,up:1,none:3,select:1,none:6,select:1,none:60" --quit-after 1100
  ```

  The host logs "You gave Guest 5000 RU.", the guest docking ("Saw guest-1 Starbase at frame 387" on this run; the frame varies a little), then its outfitting keys: Outfit Starship, Modules, a crew pod into module slot 0 ("guest-1: starbase Select: Acted"), a second into slot 2 ("Acted"), a third into slot 3 refused at the cap ("Refused"; the guest is told "That can't be done."), Esc, up to the jets, turning jets into jet slot 1 ("Acted"), up to the drives, a thruster into drive slot 0 ("Acted"). The save (version 12, `escort_caps` the placeholders) has the guest's escort with 3 thrusters (drive slots 0, 5 and 6), 3 jets (0, 1 and 6), crew pods in module slots 0 and 2, the kit's fuel tank in 1, its storage bay in 8 and its gun in 15, and 0 resource units (2,000 + 2,000 + 500 + 500); the guest's report has its fuel capacity 6000 (the reserve and one tank, in hundredths).
- That save with `crew_pods` set to 3, then `--host --name Host --load three.json --save three-out.json --report three-report.json --quit-after 30`: it loads ("Loaded ..."), and `three-out.json`'s caps are thrusters 8, jets 6, fuel tanks 4, crew pods 3.
- That save with `crew_pods` set to 20, loaded the same way: not loaded. The error is logged ("ERROR: System.IO.InvalidDataException: Bad settings: escort caps: an escort's most crew pods, 20, isn't from 0 to 15 (below the flagship's 16) (Parameter 'EscortCaps')"), no host starts (the report's `mode` is `client`, and no save is written), and the game runs on to `--quit-after` and exits as usual, as with any other bad setting in a save.

## What's different from UQM

UQM's escorts are combat-only ships with no modules; guests' escorts are small flagships (`docs/differences.md` row 9). Now they buy the flagship's modules but its unique two, with caps; their kit's crew counts toward their most crew beside their crew pods, where UQM's flagship holds crew in pods only, and the starbase's screens draw the crew in the pods first, the kit's past them not drawn (UQM's picture has no seats for them).

## Known limits

- **The caps are placeholders**, to be set by playtesting (`docs/design.md`'s open item), and set only in the save.
- **Selling the kit is a money source without end.** A new guest's escort costs nothing to make: any player id the host hasn't seen gets a fresh kit, and a client picks its own id (`--id`). The whole kit sells back as UQM's flagship sells its starting modules, so joining under new ids, selling the kit and giving the units to another player can be repeated without limit: about 6,000 resource units an id at a new game's prices (the thrusters, jets and fuel tank 2,500 of it since this item; the lander, storage bay, gun, fuel and crew already sold). It's left for the owner to decide (`docs/handoff.md`).
