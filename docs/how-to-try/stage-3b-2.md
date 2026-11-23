# How to try it: stage 3b-2, the races

Stage 3b-2 ports the races' talks left after stage 3b-1, the worlds that start them and what they do to the game (`docs/plans/stage-3b-2.md`). It's built an item at a time; this page grows with it (the plan's item 12 finishes it). Item 1 (what talks can do: alliances, ships given and taken, the world talks, the Thraddash's body count, `--start-state`) is tried in `docs/how-to-try/stage-3b-1.md`'s headless runs.

## Set up

As for stage 3b-1 (`docs/how-to-try/stage-3b-1.md`): build the game, give a testing host a `--save` file of its own. The commands below are written `godot --path game -- ...`; use the Godot 4 .NET (mono) build. `--meeting-replies I,I,...` gives the host's replies by their place among those offered, from 0; `--start-state NAME=VALUE,...` sets story fields first, to reach what the Melnorme's trading (item 4) and stage 3c's events will set in play.

## Run the tests

```
CRATERIS_CONTENT=~/Downloads/uqm-0.8.0-content.uqm python3 tools/run_tests.py   # every topic
```

Item 4's tests: `MelnormeTests` (topic `world`: trading with the answerer's own credits, a gift mid-talk, the catalog, fuel by the ship's own tanks, the bio sale, the anger, the rescue's strip of a guest's escort, the strip offer's refusal, the rescue-only talk beside another player's, the rescue fields and broadcaster each player's own and saved, the super giants' ships), the script's recorded paths in `TalkTests` (topic `talk`: `tests/golden/talks-melnorm.json`), the bio data's count-down in `MineralSellingTests` (topic `drawing`).

Item 3's tests: `CreditsAndFuelTests` (topic `trading`: the balances, gifts of credits and fuel, fuel's nearness and room, the wire, the save, the giving screen's rows), `RainbowWorldTests` (topic `world`), `MeetingTests.FuelGivenDuringTheSpinIntoAGroupStays`, and the giving screen's drawing in `GivingScreenTests` (topic `drawing`).

Item 2's tests: `HomeTalkTests` (topic `world`: Spathiwa's moon, the Evil Ones, the Slylandro gas giant), the two scripts' recorded paths in `TalkTests` (topic `talk`: `tests/golden/talks-spahome.json`, `talks-slyhome.json`) and `WorldTalkTests` (the worlds' table, now with these two ported).

## Item 2: the Spathi and the Slylandro at home

### Spathiwa's moon

Orbiting Spathiwa's moon (star 222, Spathiwa planet 0, its moon 0), while the Spathi haven't shielded themselves, meets the Spathi at home against their endless fleet instead of an orbit: the encounter screen (unless they're at peace with you), then the Safe Ones' council on the communication screen. The ship is never put in orbit there. Spathiwa itself is orbited as any world.

1. **The password.** They ask for the Secret Cypher. Without it (from the Melnorme, whose trading comes with item 4, or from Fwiffo on Pluto, stage 3c) every answer is wrong and they fight; after a fight they're angry with you.
2. **The council.** With the password: their council, the alliance offered and refused, and a test, the quest: wipe the Evil Ones off Spathiwa.
3. **The Evil Ones.** Spathiwa has 32 of them. A lander bringing up a stunned one marks them examined (`SPATHI_CREATURES_EXAMINED`); the last of the 32 brought up eliminates them (`SPATHI_CREATURES_ELIMINATED`), whichever players brought them up. Saying you killed them all when you haven't is a lie they find out; the second time they hate you for good.
4. **The party and the alliance.** With the Evil Ones gone, they party; then the alliance: the Spathi's ships go on sale at the starbase, every other player is told ("Host made an alliance with the Spathi: their ships are for sale at the starbase."), and the shield event is queued six months on, set aside when it falls due until stage 3c runs the calendar.
5. **Fwiffo.** At the council, with Fwiffo found on Pluto, the replies differ by whether his Eluder is in the host's fleet (whoever is talking): a hostage, or "he's dead".

Tried headless, each with `--host --no-upnp --id host --name Host --save FILE`:

- `--start-orbit 222:0:0 --encounter-choice hail --meeting-replies 1 --quit-after 900`: "Starting in orbit: star 222 planet 0 moon 0", "host at the encounter screen: an infinite fleet of Spathi ships, hail or attack", "Spathi: What is the Secret Cypher?", the six replies (the first with the captain's and ship's names: "Attention aliens: I am Zelnick acting Captain of the starship Vindicator."), "host chose reply 1", "You do not know the Secret Cypher!...", "The talk with the Spathi ends: hostile", "host fights the Spathi".
- `--start-state KNOW_SPATHI_PASSWORD=1,SPATHI_HOME_VISITS=7 --start-orbit 222:0:0 --encounter-choice hail --meeting-replies 0,0,0,0,0,0,0,1,2 --quit-after 900` (the Melnorme's password, before any visit): the council, the alliance offered ("Well, that would probably work but I have a better idea -- a test"), "You must wipe the Evil Ones from the face of Spathiwa!", their history, "We will await your return with great anticipation.", "The talk with the Spathi ends: peace", "host's meeting with the Spathi is over".
- `--start-state SPATHI_PARTY=1,SPATHI_MANNER=3 --start-orbit 222:0:0 --meeting-replies 0,0,0,0,2 --quit-after 900` (the Evil Ones gone, the party): no encounter screen (at peace), "Oh joy! Oh happiness!...", the haggling, "A Spathi delegation will depart immediately for the planet Earth.", "The talk with the Spathi ends: peace". The save has the Spathi `good` among the alliances and `SPATHI_SHIELD_EVENT` queued for 2155-08-17.

### The Slylandro gas giant

Orbiting the Slylandro's gas giant (star 497, planet 3) meets the Slylandro at home: no ships, no encounter screen, straight to the communication screen, always at peace. While a player talks to them, the probes are theirs too (the same lock): nobody else talks to a probe meanwhile. The gas giant is never orbited.

Their questions come in stacks: the visitors they've had (the Taalo, the Precursors), the Slylandro themselves, the humans, and once they've mentioned them, the probes. Once a probe has attacked you (`PROBE_EXHIBITED_BUG`) and you've learnt how the probes' programming works, you can show them the bug, and ask for the self-destruct code: it's aboard from then on (`DESTRUCT_CODE_ON_SHIP`: a probe met is destroyed by it, an instant victory), and the probes ramp down: `SLYLANDRO_MULTIPLIER` up by one, then at once down by one, and by one again every 23 days until none come. On the next visit they greet you with the recall program, once.

Tried headless:

- `--start-orbit 497:3 --meeting-replies 3 --quit-after 600`: "host talks to the Slylandro", "Hello visitor! We are the Slylandro...", the four replies (the first with the names: "I am Zelnick, Captain of the Earth starship Vindicator. What the heck are you?"), "Goodbye human fluid sack!", "The talk with the Slylandro ends: peace", "host's meeting with the Slylandro is over".
- `--start-state SLYLANDRO_HOME_VISITS=1,SLYLANDRO_STACK1=7,SLYLANDRO_STACK2=1,SLYLANDRO_STACK3=3,SLYLANDRO_STACK4=2,PLAYER_KNOWS_PROBE=1,PROBE_EXHIBITED_BUG=1,SLYLANDRO_STACK5=1,SLYLANDRO_STACK6=1,SLYLANDRO_STACK7=3,SLYLANDRO_STACK8=3,SLYLANDRO_STACK9=3,PLAYER_KNOWS_PROGRAM=1,PLAYER_KNOWS_PRIORITY=1,PLAYER_KNOWS_EFFECTS=1 --start-orbit 497:3 --meeting-replies 1,0,0,0,0,0,3,1 --quit-after 600` (everything asked but the bug): "You return with great speed.", the probe's bug argued through to "How can we stop the probes from destroying all life in the galaxy?!", "WHY YES, THERE IS!", "Goodbye human fluid sack!". The save has `DESTRUCT_CODE_ON_SHIP` 1, `SLYLANDRO_MULTIPLIER` 1 (a new game's 1, up to 2 and at once down) and `SLYLANDRO_RAMP_DOWN` queued 23 days on (2155-03-12).

The Evil Ones weren't tried by hand (a lander must stun and bring up each one): `HomeTalkTests` cans one and brings it up, with none and with the other 31 taken before.

### What's different from UQM

Each is a row of `docs/differences.md`: Spathiwa's moon's talk with several players (row 74: under the Spathi's lock, as the Ilwrath homeworld's), the worlds that start a talk (row 27), the events their talks queue set aside until stage 3c (row 66). The talks are each answerer's: the names spliced into the replies are the answering ship's, and Fwiffo is looked for in the host's fleet whoever answers (the plan's question 4).

### Known limits of item 2

- **The password** comes from the Melnorme (item 4) or Fwiffo (stage 3c): until then only `--start-state` reaches the council.
- **The Spathi never shield themselves** (their event is stage 3c's), so Spathiwa's moon keeps talking after the alliance.
- **The alternate "Safe Ones" song** is chosen but nothing plays music yet.

## Item 4: the Melnorme

The Melnorme trade from their ships: one waits at each of the nine super giants, and in hyperspace they come to a ship out of fuel (30% a day, 100% after the broadcaster, stage 3c's device; `--start-state USED_BROADCASTER=1` sets the host's use until then). They start at peace: no encounter screen, straight to the communication screen, the status area's message showing the answerer's credits ("1234 Cr").

1. **The first meeting.** Small talk, and the bluster ("trade is for the weak!"), which can anger them; "Let's get down to business" starts the trading.
2. **Trading**, with the answering player's own credits, read live: a friend's gift of credits mid-talk counts at once. Fuel up to the answering ship's own tanks, a credit a unit. The technology catalog, 150 credits an item: the first item the answering ship lacks; a module (the blaster, point defense, the high-efficiency fuel system, tracking, the cannon, the Shiva furnace) goes on sale at the starbase for everyone, and every other player is told ("Host bought the blaster from the Melnorme: it's for sale at the starbase."), a lander upgrade is the answering ship's own. Information, 75 credits a piece (one piece sells the Spathi's password).
3. **Selling** bio data (two credits a unit, counted down on the cargo display left of the communication screen; any key sells the rest at once) and the rainbow worlds the group has found (500 credits each, paid once, to whoever sells them first).
4. **Anger.** Angering them is shared: every other player is told, and they fight. Any player can calm them by having their ship stripped whole (`ok_strip_me`).
5. **The rescue.** A ship with nothing to trade (no fuel, bio data, credits or unsold rainbow worlds) is offered fuel to get home for parts of its own ship (its own slots: a guest's escort's); taking it strips those parts and adds the fuel up to the tanks' room, so fuel a friend gave meanwhile stays. While the offer stands, minerals can't be given to that player ("Not now: the Melnorme are offering to strip their ship, storage bays and all."). While another player trades with the Melnorme, a stranded player met by another Melnorme ship is rescued all the same (once the players have got down to business with them and while they aren't angry): straight to the rescue, without the greeting.

Tried headless, each with `--host --no-upnp --id host --name Host --save FILE`:

- `--start-at 4000:4000 --start-encounter melnorme --start-state MET_MELNORME=1,MELNORME_YACK_STACK2=6,WHY_MELNORME_PURPLE=1,TRADED_WITH_MELNORME=1,MELNORME_TECH_PROCEDURE=1 --start-credits 500 --meeting-replies 0,1,0,1,2 --quit-after 900`: "host talks to the Melnorme", "How nice to see you again, Captain.", the purchases' replies, "...plans for building blaster weapons twice as powerful as your ion-bolt guns.", bought ("Please remember that these weapons consume energy..."), the next item (the lander's speed) declined, then the information menu. The save has the host's 350 credits and the blaster's price (80, on sale at the starbase).
- `--start-at 5000:4000 --start-fuel 0 --start-encounter melnorme --start-state MET_MELNORME=1,MELNORME_YACK_STACK2=6 --meeting-replies 0,0 --quit-after 900` (`--start-fuel`, new: the host's fuel in units): "...you are immobile in HyperSpace with no fuel reserves...", "Would you like us to help you at this time?", yes: "...we will take: one of your Planet Landers, two of your Fusion Thrusters, one of your empty Storage Bays", accepted: "The exchange of fuel for equipment is complete.", "The talk with the Melnorme ends: peace". The save has the flagship's fuel 43.30 units, no lander, no thrusters, its storage bay gone, and the host's own rescue count (`"melnorme": {"rescue_refused": 0, "rescue_count": 1, ...}`).
- Windowed, the first run with `--start-credits 1234 --screenshot FILE.png --quit-after 400`: the Melnorme on the communication screen, "1234 Cr" in the status message box to its left.

The rescue-only talk beside another player's, the bio data's count-down and gifts mid-talk weren't run by hand (two players at once, and bio data needs a lander's catch): `MelnormeTests` runs them, and `MineralSellingTests` the count-down's drawing calls.

### What's different from UQM

`docs/differences.md` rows 80 (the rescue with several players: the rescue-only talk, the strip as a change, the fields each player's own) and 81 (trading with several players: the catalog by the answering ship, modules for everyone, the bio count-down), and rows 17, 19, 68 and 79 updated.

### Known limits of item 4

- **The bio count-down** plays before the reply's lines (UQM's comes after the first of them), as the minerals' at the starbase does; a unit a millisecond (UQM's loop has no wait of its own).
- **The broadcaster** is stage 3c's device: only `--start-state USED_BROADCASTER=1` (the host's) brings the Melnorme at 100% until then.
- **Saves:** item 4's rescue fields and broadcaster's use are version 14's optional part (`players[].melnorme`); item 3's code loading such a save drops them.

## Item 3: Melnorme credits, fuel between players, the rainbow worlds

Each player has their own Melnorme credits (UQM keeps one balance in the game state). Until the Melnorme's trading (item 4) nobody earns any in play: `--start-credits N` gives the host some to try giving. The status panel shows them beside the resource units ("…, 0 RU, 300 Cr").

**Giving.** T opens the giving screen as before; under the resource units are two new rows: the credits ("Cr", Page Up and Down by 100) and the fuel ("Fuel", in whole units). Credits go to anyone, anywhere, at any time, to a player in a talk too (topping up a Melnorme trader, once item 4 has the trader). Fuel goes only to a player close by, both in the same system or both in hyperspace within a screen of each other, and only up to the giver's whole units and the room in the receiver's tanks; with the receiver further off the fuel row is dimmed, as the cargo rows are with the receiver outside the system, and the host refuses it: "Fuel goes only to a player close by: within a screen in hyperspace, or in the same system." A full receiver: "Their fuel tanks haven't the room." The text below the box says where the receiver is, what can go to them there and their tanks' room; while the fuel row is lit, the box's Free row shows that room too (in whole units, dimmed unless the receiver is close by; on the other rows it's their storage bays' room, as before), the one place it fits over the lander's gauges, where the line below is cut before it. A player spinning into a group they met gives only what the spin has left: the spin burns fuel as it drifts, and becomes the ship's at its end. A player in a talk, or spinning into a group they met, can be given fuel. While docked at the starbase, fuel a friend gives is drawn on the fuel gauge and in outfitting's tanks or the shipyard's blueprint.

Tried headless, the host with `--host --no-upnp --id host --name Host --save FILE` and a guest joining with `--join 127.0.0.1 --id guest --name Guest`:

- `--start-orbit 82:2:1 --guests-in-orbit --start-credits 500 --script "none:200,give:1,none:5,down:1,none:3,pageup:1,none:3,pageup:1,none:3,select:1,none:150" --quit-after 420`: the giving screen on the credits row, two Page Ups make 200, Enter gives them: "host gave guest 200 credits", the host told "You gave Guest 200 credits.", the guest "Host gave you 200 credits."; the snapshots end with the host's 300 credits and the guest's 200.
- `--start-at 4000:4000 --script "none:300,give:1,none:5,down:1,none:3,down:1,none:3,pageup:1,none:3,select:1,none:150" --quit-after 500` (the guest undocks beside the host in hyperspace): the fuel row, Page Up asks 10 units, the host's 9 whole ones given: "host gave guest 9 units of fuel", the guest's tanks from 9.98 to 18.98 units.
- Windowed, `--screenshot FILE.png`: the giving screen in orbit round Luna with its new rows, and over the lander's gauges (three lines of text fit below the box there; landing as in `docs/how-to-try/stage-2b.md`).
- Windowed over the lander's gauges with a guest (review round 1): the host with `--start-orbit 82:2:1 --guests-in-orbit --screenshot FILE.png --script "none:300,select:1,none:10,select:1,none:250,select:1,none:30,give:1,none:10,down:1,none:3,down:1,none:20" --quit-after 700` (Scan, Auto scan, Dispatch lander, then the giving screen on the fuel row) and a headless guest joining with `--quit-after 1500`: the Free row reads 50, the guest's escort's room (60 units of tanks, 10 aboard), where on the credits row (one `down` fewer) it reads 500, their storage bay's; the line below is cut at "Their tanks…".

**The rainbow worlds.** Orbiting one of the ten rainbow worlds (the first planet of each `RAINBOW_DEFINED` star) marks it found for the group (`RAINBOW_WORLD0`/`1`, as UQM's generator does), whoever orbits it; the Melnorme will buy each once (item 4). `--start-orbit` to one does it too, and so does loading a game saved in orbit around one. `RainbowWorldTests` flies a ship into one's orbit; the stars are in `data/starmap.json`.

**Saves and the wire.** Saves are version 14 (each player's credits; an older save loads with none); the protocol was 28 (29 since item 4), so an older client is refused.

### What's different from UQM

`docs/differences.md` row 19 (credits each player's, given at any time) and row 79 (fuel between players, new: UQM has one ship).

### Known limits of item 3

- **No Melnorme in item 3** (item 4 brings them, above).
- **Fuel is given in whole units** from the giving screen; a fraction of a unit left in the giver's tanks, or of room in the receiver's, can't be given.

