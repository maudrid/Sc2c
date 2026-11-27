# How to try it: stage 3c, story events and devices

Stage 3c makes the story move: the events UQM's talks queue run, the story worlds give up what's on them, the devices do what they do, and the places 3b-2 left shut open (`docs/plans/stage-3c.md`). It's built item by item; this page grows with each, in the plan's order. Each item says what to try, the headless runs that show it (each checked by hand as the item was built), what's different from UQM and what's left.

## Set up

As for stage 3b-2 (`docs/how-to-try/stage-3b-2.md`): build the game (`dotnet build game/Crateris.Game.csproj`) and point `CRATERIS_CONTENT` (or `--content PATH`) at the UQM content package. The commands below are written `godot --path game -- ...`; use the Godot 4 .NET (mono) build, with `--headless` for the headless runs. Each run is a host: give it `--host --no-upnp --id host --name Host --save FILE`, the save a scratch file of its own (without a `--save`, `--start-state`, `--start-date` and the like given with no `--start-*` place change the default game).

The testing options this stage adds (`game/scripts/Options.cs`'s summary lists them all):

- `--start-date YYYY-MM-DD` (item 1): the clock moved on to that day, no earlier than the game's, each day passing as the starbase's two weeks pass: its events run (the location gates asking where the players are as the game has them), the races' fleets grow and move, but no hyperspace encounters are rolled. Its log line names the events run (the daily encounter event left out), those a location gate held (one for each time it held one) and those set aside. It's applied after `--start-state`, `--start-devices`, `--start-allied` and `--start-fleet` and before the place (`--start-at`, `--start-orbit` and the rest). With `--load` it moves a saved game's clock over the events its talks queued.

## Run the tests

```
CRATERIS_CONTENT=~/Downloads/uqm-0.8.0-content.uqm python3 tools/run_tests.py   # every topic
```

Item 1's tests: `ClockEventsTests` (topic `story`: every event the port runs replayed day by day against UQM's own `clock.c` and `gameev.c`, `tests/golden/clock-events.json`, the gates with them), `CalendarEventsTests` (topic `story`: the events set aside before 3c run once in date order from where the clock is, on loading too; the gates with several players, flying, in orbit, landed, in a talk, and a player away and a wreck not holding them; the notices; a meeting with the Spathi going on after their shield; the Kohr-Ah's ship at the Zoq-Fot-Pik's world and their thanks after it, also when a guest destroys it after the host left its battle or their lock is held (the rescue at the next orbit); a save's events held as it loads by a saved player's ship; the same day's set-aside and queued events in order; the rebellion check's groups; the Arilou's counter; the rebellion check after a battle and before the war; `--start-date`; an event's changes taken back), `OpeningTests` (the starbase's two weeks taken back with what their events did, the crew's price among it), `VuxYehatTests` (the revolt's event at once, its notice, the royalist group fleeing after), `WorldTalkTests` (the Kohr-Ah's entry at the Zoq-Fot-Pik's world) and `ArilouSyreenChmmrTests` (the Arilou's look at the Umgah, run ten days on).

The recording is remade with `python3 tools/extract/golden_uqm.py --uqm ../uqm-0.8.0/sc2 --content PACKAGE --only clock-events` (a few seconds; code only).

Item 2's tests: `StoryPauseTests` (topic `story`: a test's sequence taking a story pause: its skip held by the host's pause, everyone frozen through its fade, its days running their events with no encounter rolled, then over; a story talk after the days shown to every player, answered and paged by its replier alone, the replies passing on as they leave, its closing lines after; a talk-only pause, the starbase's opening waiting under it; a pause's talk refused unless story; saves during and after the skip; review round 1: a reply for the replier's meeting held through a pause's skip and through the visit's two weeks, a game over during the days starting no talk, a player in their meeting's talk shown the pause's closing lines before it again), `OpeningTests` (the two weeks rolling no encounter for a guest in hyperspace while the fleets move as the same days move them), `TalkScreenTests` (topic `talk`: every recorded start's screen coming in as UQM's script set it, `"intro"` in the talks' recordings; the hypnotised captain's Ur-Quan and the emerging Chmmr; the starbase's talks' intros), `MeetingWireTests` (topic `world`: the intro and the story pause's step on the wire and their bounds; a story pause on two clients, the skip not shown as a talk, the talk on both, the replier's client answering; a client keeping its meeting's talk, its encounter screen, its flight or its talk with the commander from the menu under a talk-less skip; the host's log, every pause from start to end, one taken and over between two of its frames too), `ScreenFadeTests` (topic `drawing`: the whole screen's fade to black and back).

## Item 1: the calendar's engine and the simple events

The clock's events run as UQM's `gameev.c` runs them, except the races' missions and the Kohr-Ah's victory and genocide (items 3 and 4), which are still set aside on their day:

1. **The Arilou's portal** opens on the 17th of each month and closes three days later (`ARILOU_SPACE`; its counter, `ARILOU_SPACE_COUNTER`, counts up to 9 as it opens and down as it closes, a step each world frame). Nothing is drawn yet: the portal itself, and the way through it to QuasiSpace, are item 9's.
2. **The Arilou's look at the Umgah** runs ten days after their homeworld's talk asks it (QuasiSpace's, item 9; `--start-state` reaches it): asked before, they have no news; after, they've been.
3. **The Shofixti come back** two months after Tanaka is recruited: allied (their ships on sale at the starbase), and crew 2 RU cheaper there, the crew bought counted afresh.
4. **The Spathi shield themselves** six months after their alliance at Spathiwa's moon: no longer allies, their fleet gone, their moon's talk over (`genspa.c` starts none once they're shielded).
5. **The Zoq-Fot-Pik's distress** comes three months after their alliance: the Kohr-Ah attack their world. Their system has no groups meanwhile, and orbiting their world meets the Kohr-Ah's one ship first (hostile: a battle). Destroy it and the Zoq-Fot-Pik's talk follows at once, the ship still by the world: their thanks and 4 ships for the host's fleet. Retreat, and the ship stays by the world, the Kohr-Ah there still. With several players, the Kohr-Ah's ship destroyed by anyone (another player in its battle after its player left it, say) is recorded gone, saved with the game: if their thanks can't follow at once (the meeting's player gone from the battle or wrecked, or another player talking to the Zoq-Fot-Pik meanwhile), the next orbit of their world by anyone brings the Zoq-Fot-Pik's rescue talk, with no new Kohr-Ah ship, and until then their death's day passes without killing them. The record goes as soon as the distress is over (the rescue's thanks). Not rescued within six months, the Zoq-Fot-Pik are destroyed.
6. **The Yehat's civil war**: sending them a Shofixti ship (3b-2 item 6) runs the revolt's event at once: the royalists' fleet cut to two thirds, the rebels' as large, centred at (5150, 0); the rebels' groups fly from then on. After a meeting in a system with no battle, the Yehat groups that stood down from the touch flee the system (the rebellion check).

With several players:

- **The location gates** (the plan's question 1): the Spathi's shield and the Zoq-Fot-Pik's distress and death wait 7 days at a time while any player is in that race's system, flying, orbiting, landed, docked, or in a meeting, talk or battle there. A player away, or a wreck, doesn't hold them; but while a save loads, before anyone is back in the game, every saved player's ship holds them where the save has it.
- **Events run on time** (question 2): a meeting with a race the calendar changes meanwhile goes on as it began, its battle fought as begun.
- **Everyone is told** of an event that changes a race (the Shofixti's return, the Spathi's shield, the Zoq-Fot-Pik's distress and destruction), a player alone too, in the status line: "The Spathi have shielded their world: they're no longer allies, and their ships are gone." The Yehat's civil war is told to everyone but the player whose talk started it: "Host started the Yehat's civil war: the rebels have a fleet of their own." The Arilou's portal and their look at the Umgah aren't told (they change no race).
- **Events set aside** by an earlier stage's code (a save from before 3c) run once as the save loads, in date order, what they queue counted from that day.
- **The opening taken back** (a starbase visit undone, stage 3a) takes back what its two weeks' events did, the crew's price too.

Tried headless, each with `--host --no-upnp --id host --name Host --save FILE` (a scratch file; the later runs `--load` the same file):

- `--start-date 2155-03-18 --quit-after 48`: "Starting on 2155-03-18: 29 days passed, 1 event run (ARILOU_ENTRANCE_EVENT)". The save has `ARILOU_SPACE` 1, `ARILOU_SPACE_COUNTER` 9 and `ARILOU_EXIT_EVENT` queued for 2155-03-20.
- `--start-orbit 293:0 --encounter-choice hail --meeting-replies 1,0,0,0,3,0 --quit-after 1500` (the Zoq-Fot-Pik's homeworld): "We are The New Alliance of Free Stars' flagship Vindicator from Earth...", their species, what they seek, their nightmare, "We want to help. What can we do?", "You are under attack by the Ur-Quan, our enemy as well. Therefore, speaking for all the People of Earth, I accept your offer.", "The talk with the Zoq-Fot-Pik ends: peace". The save has them `good` and `ZOQFOT_DISTRESS_EVENT` queued for 2155-05-17.
- `--load FILE --start-date 2155-05-18 --quit-after 48`, the host still in their system: "Starting on 2155-05-18: 78 days passed, 5 events run (ARILOU_ENTRANCE_EVENT, ARILOU_EXIT_EVENT, ARILOU_ENTRANCE_EVENT, ARILOU_EXIT_EVENT, ARILOU_ENTRANCE_EVENT), 1 event held by a player in their race's system (ZOQFOT_DISTRESS_EVENT)"; the save has `ZOQFOT_DISTRESS_EVENT` queued again for 2155-05-24 and no `ZOQFOT_DISTRESS`.
- The same save with the host moved first (`--load FILE --start-orbit 82:3 --quit-after 2`, Mars), then `--load FILE --start-date 2155-05-18 --quit-after 48`: "host: The Zoq-Fot-Pik are in distress: the Kohr-Ah are attacking their world."; the save has `ZOQFOT_DISTRESS` 1 and `ZOQFOT_DEATH_EVENT` queued for 2155-11-17.
- From there, `--load FILE --start-orbit 293:0 --encounter-choice attack --quit-after 300`: "host at the encounter screen: 1 Kohr-Ah ship, hail or attack", "The talk with the Kohr-Ah ends: hostile", "host fights the Kohr-Ah", "In battle against the blackurq." (the battle isn't won headless: `CalendarEventsTests` wins it, and the Zoq-Fot-Pik's thanks follow).
- From there instead, `--load FILE --start-date 2155-11-18 --quit-after 48`: "host: The Zoq-Fot-Pik have been destroyed."; the save has them `dead`, strength 0, `ZOQFOT_DISTRESS` 2.
- `--start-state SPATHI_PARTY=1,SPATHI_MANNER=3 --start-orbit 222:0:0 --meeting-replies 0,0,0,0,2 --quit-after 900` (the Spathi's party and alliance, as in `docs/how-to-try/stage-3b-2.md`): `SPATHI_SHIELD_EVENT` queued for 2155-08-17. Then `--load FILE --start-date 2155-08-18 --quit-after 2`, the host still at Spathiwa: the shield waits, queued again for 2155-08-24. Then `--load FILE --start-orbit 82:3 --quit-after 2` and `--load FILE --start-date 2155-08-25 --quit-after 48`: "host: The Spathi have shielded their world: they're no longer allies, and their ships are gone."; the save has `SPATHI_SHIELDED_SELVES` 1, the Spathi `bad`, strength 0.
- `--start-state SHOFIXTI_VISITS=1 --start-fleet shofixti --start-at 4000:4000 --start-encounter yehat --encounter-choice hail --meeting-replies 3,0 --quit-after 900` (the revolt, as in `docs/how-to-try/stage-3b-2.md`): "...we shall TEAR THE QUEEN FROM HER THRONE!", "The talk with the Yehat ends: peace". The save has nothing set aside, the royalists' strength 90 (two thirds of 135) and the rebels' 90 at (5150, 0), known (tracked). Alone, the host isn't told (it started it).

Not tried by hand: the Shofixti's return (Tanaka's recruitment needs the Maidens, item 5's pickup; `CalendarEventsTests` and the recording run it), several players' gates and notices (the tests have them), a save from before 3c loading (`CalendarEventsTests` makes one).

### What's different from UQM

Each is a row of `docs/differences.md`: the calendar's co-op rules (row 88: the gates with several players, events while their race is met, who is told), the events still set aside (row 66, partly closed), the rebellion check (row 72), the notices (row 76), the starbase's two weeks taken back (row 3). The Arilou's counter steps once a world frame, as the port's one clock runs in every activity (row 1), where UQM's steps each hyperspace frame.

### Known limits of item 1

- **The portal** opens and closes only as story fields: nothing is drawn, and it can't be entered (item 9).
- **The missions and the Kohr-Ah** (the Pkunk's, the Thraddash's, the Ilwrath's, the Mycon's, the Utwig and Supox's; the Kohr-Ah's victory and genocide) are still set aside: items 3 and 4.
- **The fleets aren't on the screen**: the rebels' and the others' spheres change, but there's no star map; the status panel's fleet lines come with item 3 (the plan's question 8).
- **The Shofixti's return** is reached in play only once the Maidens can be picked up (item 5); until then a test or a save queues it.

## Item 2: story pauses generalised

UQM skips time in three places (the starbase's two weeks, the bomb's 14 days, the Talking Pet's 15), each a two-second fade to black and then the days at once, and shows a few talks' screens fading in. The port's one story pause, the starbase's opening, is now one any sequence can take (`World/StoryPause.cs`): a time skip (`World/TimeSkip.cs`), a story talk on everyone's screen, or both, the talk after the days, and then the sequence that took it goes on. The starbase's two weeks are its first user; the Black Ur-Quan's last talk (item 4), the Pet's jump (item 10) and the bomb's days (item 11) come later.

1. **The two weeks, with a friend.** Bring the Lunar base to the commander (`docs/how-to-try/stage-3a.md`, or `--start-visit base-report`), with a friend flying in hyperspace. After the alliance's name, every player's whole screen fades to black over two seconds, whatever it shows (the friend's hyperspace view too), and the game waits. Then the 14 days pass at once: the date jumps, each day's events run (the Arilou's portal, a Shofixti return due then), the races' fleets grow and move, but no group of alien ships is rolled for anyone, the friend in hyperspace included (the plan's question 3). The allied commander's screen then fades in from black over two seconds (UQM's `CIM_FADE_IN_SCREEN`), on every screen.
2. **A visit straight into the two weeks** (the starbase open, the base aboard: `--start-state STARBASE_AVAILABLE=1,MOONBASE_ON_SHIP=1 --start-visit`): there's no talk before it, so each player's own screen fades under it as it was (their flight, their encounter screen, their own meeting's talk), with no keys for a talk: no reply can be made then, but one already on its way as the skip comes is kept and made as the pause ends. (A docked player's talk with the commander from the menu stays on their screen the same way under a general talk-less pause, a later item's; the two weeks can't begin while another player talks to him.)
3. **The talks' intro.** A talk's screen comes in as UQM's `SetCommIntroMode` says: the allied commander after the two weeks fades in (2 s), the Chmmr emerging fades in (2 s: `--start-state CHMMR_EMERGING=1 --start-at 4000:4000 --start-encounter chmmr`), the Ur-Quan before the hypnotised captain fades in (5 s: `--start-state PLAYER_HYPNOTIZED=1 --start-at 4000:4000 --start-encounter urquan --encounter-choice hail`). With no skip before them their screens aren't dark, so nothing shows yet: the Chmmr's comes from the Sun Device's white flash (item 8 and 11) and the Ur-Quan's from the Pet's jump (item 10).

With several players:

- **A time skip is a story pause for everyone, its fade on every screen** (the plan's co-op rules): everyone waits, input meant for then is dropped, and nothing moves; the clock jumps for all.
- **No hyperspace encounters during a skip** (question 3): a player in hyperspace meets no new group from those days; the fleets still move and every event still runs.
- **The game over during a skip's days** (the Kohr-Ah's genocide, item 4's) ends it there for everyone: no talk follows, as in UQM.
- **A story talk outside the starbase** is shown to everyone and answered by the player whose sequence started it, the replies passing to the next player (the host first) if they leave, as the opening's (row 2). Nothing in play starts one yet (item 4's Black Ur-Quan is the first); `StoryPauseTests` and `MeetingWireTests` run one with the commander's talk.

Tried headless, each with `--host --no-upnp --id host --name Host --save FILE` (a scratch file):

- `--start-state STARBASE_AVAILABLE=1,MOONBASE_ON_SHIP=1 --start-visit --quit-after 200`: "Starting in the TimePassing talk at Earth's starbase", "Time passes: every screen fades to black, then 14 days pass (no encounters meanwhile)", "14 days passed: it's 2155-03-03". The save (made as it quits, in the allied commander's talk) is of the game before the visit: 2155-02-17, `MOONBASE_ON_SHIP` still 1.
- The same with `--script "none:200,select:1,none:200" --quit-after 500`: after the two lines above, "Reply 0: Before proceeding I need some additional background information.", "host: reply 0": the allied commander's talk after the days, answered.
- `--start-state CHMMR_EMERGING=1 --start-at 4000:4000 --start-encounter chmmr --quit-after 300`: "host talks to the Chmmr", "Chmmr: WE ARE FREE! ...": the talk whose screen fades in over 2 s (its view's `TalkScreen.Intro`; headless draws nothing).
- `--start-state PLAYER_HYPNOTIZED=1 --start-at 4000:4000 --start-encounter urquan --encounter-choice hail --quit-after 300`: "host talks to the Ur-Quan", "Ur-Quan: ...in addition, your refusal to answer our transmissions is taken as an insult...": the hypnotised captain's talk, fading in over 5 s.

Not tried by hand: the fade and the fade-in on a screen (drawn by the client's `ScreenFade` over every screen; `ScreenFadeTests` checks its timing), a friend in hyperspace during the two weeks (`OpeningTests` and `MeetingWireTests` have it).

### What's different from UQM

Rows 2 (story pauses outside the starbase), 3 (the skip: every player's whole screen fades; no encounter rolled), 67 (no rolls during a skip) and 83 (the intro: the fade-in drawn, the crossfades cut). UQM's fade is `FadeAllToBlack` over its whole screen, as the port's now is (before, only the communication screen's area faded).

### Known limits of item 2

- **Nothing but the starbase takes a story pause in play yet**: the Black Ur-Quan's last talk (item 4), the Pet's jump (item 10) and the bomb's days (item 11) will.
- **The crossfades** (UQM's default for every talk, 31/60 s over the space window, and the commander's whole-screen ones before the alliance) are sent but cut at once, as before.
- **A story pause isn't saved**: a save made during one is of the game as it stands (its days in it once they've passed); the starbase's opening is still saved as before it began.
