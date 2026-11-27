// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.
//
// Follows The Ur-Quan Masters 0.8.0 (src/uqm/planets/generate/: the story
// worlds' generateOrbital, generateEnergy, pickupEnergy and pickupLife in
// gensol.c, genvux.c, genspa.c, genburv.c, genutw.c, genorz.c, genthrad.c,
// genmyc.c, genilw.c, genpet.c, genyeh.c, genzoq.c, gensup.c, gendru.c,
// genpku.c, genand.c, genwreck.c and genvault.c; genchmmr.c's starbase
// report; gendefault.c's GenerateDefault_landerReport and
// GenerateDefault_landerReportCycle; genrain.c's
// GenerateRainbowWorld_generateOrbital), Copyright Paul Reiche, Fred Ford
// and Toys for Bob, Inc. 1992-2002, and the UQM contributors, under the
// GNU GPL 2 or later.

using Crateris.Core.Data;
using Crateris.Core.Planetside;
using Crateris.Core.Systems;
using Crateris.Core.World;

namespace Crateris.Core.Story;

/// <summary>
/// A story world's one node and what taking it does (stage 3c item 5, the
/// single-node pickups): the world (its star's special generator, the
/// planet, the moon or null), the node (its scan and number), the story
/// field that says it's been found (the generator's "already picked up";
/// null for the egg cases, which have only the node's retrieved bit), the
/// device it gives (its field: held by ships, the finder's; or shared, the
/// Lunar base), the sheet it's drawn and touched with on the surface (null
/// for a creature, drawn as its kind), its lander report (a string table),
/// and the other story fields the pickup sets, in the generator's order.
/// </summary>
public sealed record StoryNode(string Special, int Planet, int? Moon, ScanType Scan, int Node, string? Found,
    string Device, SurfaceSheet? Sheet, string Report, IReadOnlyList<(string Field, int Value)> Also)
{
    /// <summary>Whether this is the node's world.</summary>
    public bool On(string? special, WorldId world) => special == Special && world.Planet == Planet && world.Moon == Moon;

    /// <summary>Whether the story has it found (its generator's "already picked up": no node, no sheet, no report).</summary>
    public bool IsFound(StoryState story) => Found is { } f && story.Get(f) != 0;
}

/// <summary>
/// A ship's place in the lander report of the world it orbits (UQM's
/// DiscoveryString, the planet's for one orbit visit): the string table
/// and the entry the next report shows. Each ship in orbit has its own (the
/// stage 3c plan's question 10), made as it goes into orbit
/// (<see cref="StoryWorlds.Discovery"/>), moved on or dropped as reports
/// are shown (<see cref="Finder"/>), gone as it leaves orbit.
/// </summary>
public sealed record ReportPlace(WorldId World, string Resource, int Entry);

/// <summary>
/// What a story world's pickup does to the lander that touched the node and
/// its ship (the stage 3c plan's question 10): the report shown, by the
/// ship's place in the world's text (GenerateDefault_landerReport, shown
/// once and the text gone; GenerateDefault_landerReportCycle, the next entry
/// next time, the text gone once its entries have gone round), whether the
/// lander goes up (SetLanderTakeoff), and the devices the finder's ship is
/// given. The world applies it (GameWorld): a report with a take-off goes
/// to the finder and word of it to the other landers there, which stay; one
/// without, to the finder alone.
/// </summary>
public sealed class Finder
{
    /// <summary>
    /// A finder at its ship's place in the world's text; <paramref name="held"/>
    /// reads the finder's ship's devices (a held field's value: the vault
    /// reads its shuttle), <paramref name="energyTaken"/> is the world's
    /// retrieved energy nodes (the Androsynth's ruins keep those visited in
    /// its upper 16 bits).
    /// </summary>
    public Finder(ReportPlace? place, Func<string, int>? held = null, uint energyTaken = 0)
    {
        Place = place;
        _held = held ?? (_ => 0);
        EnergyTaken = energyTaken;
    }

    private readonly Func<string, int> _held;

    /// <summary>A held device field's value on the finder's ship (GET_GAME_STATE of it, the finder's ship's).</summary>
    public int Held(string field) => _held(field);

    /// <summary>The world's retrieved energy nodes as the lander touches the node.</summary>
    public uint EnergyTaken { get; }

    /// <summary>The ship's place in the world's report text: null once the text is gone (or there was none).</summary>
    public ReportPlace? Place { get; private set; }

    /// <summary>The report shown, null if none (no text left).</summary>
    public LanderReport? Report { get; private set; }

    /// <summary>Whether the lander is sent up (SetLanderTakeoff).</summary>
    public bool Takeoff { get; private set; }

    /// <summary>The device fields (held by ships) the finder's ship is given, in order.</summary>
    public List<string> Devices { get; } = new();

    /// <summary>The device fields (held by ships) taken from the finder's ship (SET_GAME_STATE of its field, 0: the vault's shuttle).</summary>
    public List<string> Taken { get; } = new();

    /// <summary>The energy node bits the pickup marks retrieved, the node staying (the Androsynth's ruins visited, in the upper 16 bits).</summary>
    public List<int> Visited { get; } = new();

    /// <summary>
    /// Whether the report is the group's story, told to every player (the
    /// Androsynth's chapters, the stage 3c plan's question 10), not the
    /// finder's alone.
    /// </summary>
    public bool ToEveryone { get; private set; }

    /// <summary>GenerateDefault_landerReport: the place's entry shown, and the text gone.</summary>
    public void LanderReport()
    {
        if (Place is not { } p)
            return;
        Report = new LanderReport(p.Resource, p.Entry);
        Place = null;
    }

    /// <summary>
    /// GenerateDefault_landerReportCycle: the place's entry shown, and the
    /// next one next time; once they've gone round (back to the first), the
    /// text is gone.
    /// </summary>
    public void LanderReportCycle()
    {
        if (Place is not { } p)
            return;
        Report = new LanderReport(p.Resource, p.Entry);
        int next = (p.Entry + 1) % StoryWorlds.ReportEntries[p.Resource];
        Place = next == 0 ? null : p with { Entry = next };
    }

    /// <summary>
    /// The text read from the shared story as the node is touched (the
    /// stage 3c plan's question 10): the place moved to the entry the story
    /// says now, as the generator's generateOrbital would choose it, so two
    /// players never get the same text as new; past the table's last entry,
    /// the text is gone. A text already gone stays gone.
    /// </summary>
    public void ReadAt(int entry)
    {
        if (Place is not { } p)
            return;
        Place = entry < StoryWorlds.ReportEntries[p.Resource] ? p with { Entry = entry } : null;
    }

    /// <summary>SetLanderTakeoff.</summary>
    public void SetTakeoff() => Takeoff = true;

    /// <summary>A device held by ships given to the finder's ship (SET_GAME_STATE of its field, 1).</summary>
    public void Give(string field) => Devices.Add(field);

    /// <summary>A device held by ships taken from the finder's ship (SET_GAME_STATE of its field, 0).</summary>
    public void Take(string field) => Taken.Add(field);

    /// <summary>setNodeRetrieved of an energy node bit, the node staying on the surface (genand.c's visited ruins).</summary>
    public void Visit(int bit) => Visited.Add(bit);

    /// <summary>The report is the group's story (the Androsynth's chapters): told to every player.</summary>
    public void TellEveryone() => ToEveryone = true;
}

/// <summary>What kind of story site a world with nodes that stay is (stage 3c item 6).</summary>
public enum SiteKind
{
    /// <summary>A homeworld's ruins (text.ruins, Burvixese's own): one report a visit, as the cycle goes round its one entry.</summary>
    Ruins,

    /// <summary>The Supox's, the Druuge's and the Pkunk's ruins: the artifact found at any ruin the first time, then the next entry.</summary>
    ArtifactRuins,

    /// <summary>The Androsynth's ruins: a chapter at each new ruin, in order, the group's story.</summary>
    Androsynth,

    /// <summary>The Ur-Quan wreck: the portal key the first time, then the next entry.</summary>
    Wreck,

    /// <summary>The Syreen's vault: locked, opened with the Syreen's shuttle, opened.</summary>
    Vault,
}

/// <summary>
/// A story world whose energy nodes stay on the surface as they're touched
/// (stage 3c item 6): its world, its kind, the sheet they're drawn and
/// touched with, its report (a string table), whether the generator loads
/// them now (<see cref="Open"/>: a homeworld's ruins only once its race is
/// extinct, StartSphereTracking false), and for an artifact's or the
/// wreck's, the shared field that records it found and the device it gives.
/// </summary>
public sealed record StorySite(string Special, int Planet, int? Moon, SiteKind Kind, SurfaceSheet Sheet, string Report,
    Func<StoryState, bool> Open, string? Found = null, string? Device = null)
{
    /// <summary>Whether this is the site's world.</summary>
    public bool On(string? special, WorldId world) => special == Special && world.Planet == Planet && world.Moon == Moon;
}

/// <summary>
/// The story worlds' energy and life nodes on the surface, as their
/// generators have them: the Lunar base on Luna (stage 3a) and the
/// single-node pickups (stage 3c item 5: <see cref="Nodes"/>), each with its
/// report; the ruins, the wreck and the vault, whose nodes stay (stage 3c
/// item 6: <see cref="Sites"/>); Procyon's starbase's report as a ship comes
/// to it (<see cref="ArrivalReport"/>); the nodes the story has found hidden
/// from the scan and the surface (<see cref="EnergyHidden"/>, Pluto's once
/// Fwiffo's found among them). Fwiffo on Pluto comes with stage 3c item 7;
/// until then his node shows on the scan but isn't on the surface. And the
/// story worlds' life: the Evil Ones on
/// Spathiwa (the stage 3b-2 plan's question 2), which the Spathi at home
/// ask about, and Zex's Beauty. And the orbits that mark a world: the
/// rainbow worlds (the plan's question 2), which the Melnorme buy, and the
/// shattered worlds (genmyc.c), which the Mycon can be asked about once one
/// is seen.
/// </summary>
public static class StoryWorlds
{
    /// <summary>The Lunar base's string table, its lander report.</summary>
    public const string MoonbaseReport = "text.moonbase";

    private static readonly (string, int)[] Nothing = Array.Empty<(string, int)>();

    /// <summary>
    /// The story worlds' single nodes (gensol.c's Lunar base, genvux.c's
    /// Maidens and Zex's Beauty, genspa.c's and genburv.c's broadcasters,
    /// genutw.c's bomb, genorz.c's Taalo protector, genthrad.c's Aqua
    /// Helix, genmyc.c's Sun Device and egg cases), as
    /// tests/golden/story-nodes.json records them.
    /// </summary>
    public static readonly IReadOnlyList<StoryNode> Nodes = new StoryNode[]
    {
        new("SOL_DEFINED", 2, 1, ScanType.Energy, 0, "MOONBASE_DESTROYED", "MOONBASE_ON_SHIP", SurfaceSheet.Moonbase,
            MoonbaseReport, Nothing),
        new("MAIDENS_DEFINED", 0, null, ScanType.Energy, 0, "SHOFIXTI_MAIDENS", "MAIDENS_ON_SHIP", SurfaceSheet.Maidens,
            "text.maidens", Nothing),
        new("VUX_BEAST_DEFINED", 0, null, ScanType.Biological, 0, "VUX_BEAST", "VUX_BEAST_ON_SHIP", null,
            "text.vuxbeast", Nothing),
        new("SPATHI_DEFINED", 0, 0, ScanType.Energy, 0, "UMGAH_BROADCASTERS", "UMGAH_BROADCASTERS_ON_SHIP",
            SurfaceSheet.UmgahCaster, UmgahCasterReport, Nothing),
        new("BURVIXESE_DEFINED", 0, 0, ScanType.Energy, 0, "BURVIXESE_BROADCASTERS", "BURV_BROADCASTERS_ON_SHIP",
            SurfaceSheet.BurvixCaster, "text.burvixcaster", Nothing),
        new("BOMB_DEFINED", 5, 1, ScanType.Energy, 0, "UTWIG_BOMB", "UTWIG_BOMB_ON_SHIP", SurfaceSheet.UtwigBomb,
            "text.utwigbomb", new[] { ("DRUUGE_MANNER", 1), ("DRUUGE_VISITS", 0), ("DRUUGE_HOME_VISITS", 0) }),
        new("TAALO_PROTECTOR_DEFINED", 1, 2, ScanType.Energy, 0, "TAALO_PROTECTOR", "TAALO_PROTECTOR_ON_SHIP",
            SurfaceSheet.TaaloDevice, "text.taalodevice", Nothing),
        new("AQUA_HELIX_DEFINED", 0, null, ScanType.Energy, 0, "AQUA_HELIX", "AQUA_HELIX_ON_SHIP", SurfaceSheet.AquaHelix,
            "text.aquahelix", new[] { ("HELIX_VISITS", 0), ("HELIX_UNPROTECTED", 1) }),
        new("SUN_DEVICE_DEFINED", 0, null, ScanType.Energy, 0, "SUN_DEVICE", "SUN_DEVICE_ON_SHIP", SurfaceSheet.SunDevice,
            "text.sundevice", new[] { ("MYCON_VISITS", 0) }),
        new("EGG_CASE0_DEFINED", 0, null, ScanType.Energy, 0, null, "EGG_CASE0_ON_SHIP", SurfaceSheet.EggCase,
            "text.eggcase", Nothing),
        new("EGG_CASE1_DEFINED", 0, null, ScanType.Energy, 0, null, "EGG_CASE1_ON_SHIP", SurfaceSheet.EggCase,
            "text.eggcase", Nothing),
        new("EGG_CASE2_DEFINED", 0, null, ScanType.Energy, 0, null, "EGG_CASE2_ON_SHIP", SurfaceSheet.EggCase,
            "text.eggcase", Nothing),
    };

    /// <summary>The Umgah broadcaster's report (genspa.c), whose entry the Spathi's shield chooses.</summary>
    public const string UmgahCasterReport = "text.umgahcaster";

    /// <summary>The homeworlds' ruins' report (RUINS_STRTAB).</summary>
    public const string RuinsReport = "text.ruins";

    /// <summary>The Androsynth's ruins' chapters (ANDROSYNTH_RUINS_STRTAB).</summary>
    public const string AndrosynthReport = "text.androsynthruins";

    /// <summary>The Syreen's vault's three texts (VAULT_STRTAB): locked, opened with the shuttle, opened.</summary>
    public const string VaultReport = "text.syreenvault";

    /// <summary>Procyon's starbase's report (CHMMR_BASE_STRTAB), shown as a ship comes to it.</summary>
    public const string ChmmrBaseReport = "text.chmmrbase";

    // The generators' StartSphereTracking (GameWorld's world talks start the race's talk while it lives).
    private static Func<StoryState, bool> Extinct(string race) => story => !story.WouldTrackSphere(race);

    /// <summary>
    /// The story worlds whose energy nodes stay (stage 3c item 6), as
    /// tests/golden/story-nodes.json records them: the eight homeworlds'
    /// ruins (genilw.c, genorz.c, genpet.c, genthrad.c, genutw.c, genvux.c,
    /// genyeh.c, genzoq.c) once their race is extinct (the Umgah's once the
    /// Talking Pet is taken and they aren't its zombies), Burvixese's
    /// (genburv.c); the Supox's, the Druuge's and the Pkunk's with the
    /// Ultron, the Rosy Sphere and the Clear Spindle (gensup.c, gendru.c,
    /// genpku.c); the Androsynth's (genand.c); the Ur-Quan wreck
    /// (genwreck.c) and the Syreen's vault (genvault.c).
    /// </summary>
    public static readonly IReadOnlyList<StorySite> Sites = new StorySite[]
    {
        new("ILWRATH_DEFINED", 0, null, SiteKind.Ruins, SurfaceSheet.Ruins, RuinsReport, Extinct("ILWRATH_SHIP")),
        new("ORZ_DEFINED", 0, null, SiteKind.Ruins, SurfaceSheet.Ruins, RuinsReport, Extinct("ORZ_SHIP")),
        new("TALKING_PET_DEFINED", 0, null, SiteKind.Ruins, SurfaceSheet.Ruins, RuinsReport,
            story => story.Get("UMGAH_ZOMBIE_BLOBBIES") == 0 && story.Get("TALKING_PET") != 0
                     && !story.WouldTrackSphere("UMGAH_SHIP")),
        new("THRADD_DEFINED", 0, null, SiteKind.Ruins, SurfaceSheet.Ruins, RuinsReport, Extinct("THRADDASH_SHIP")),
        new("UTWIG_DEFINED", 0, null, SiteKind.Ruins, SurfaceSheet.Ruins, RuinsReport, Extinct("UTWIG_SHIP")),
        new("VUX_DEFINED", 0, null, SiteKind.Ruins, SurfaceSheet.Ruins, RuinsReport, Extinct("VUX_SHIP")),
        new("YEHAT_DEFINED", 0, null, SiteKind.Ruins, SurfaceSheet.Ruins, RuinsReport, Extinct("YEHAT_SHIP")),
        new("ZOQFOT_DEFINED", 0, null, SiteKind.Ruins, SurfaceSheet.Ruins, RuinsReport, Extinct("ZOQFOTPIK_SHIP")),
        new("BURVIXESE_DEFINED", 0, null, SiteKind.Ruins, SurfaceSheet.Ruins, "text.burvixeseruins", _ => true),
        new("SUPOX_DEFINED", 0, null, SiteKind.ArtifactRuins, SurfaceSheet.Ruins, "text.ultron", Extinct("SUPOX_SHIP"),
            "ULTRON_CONDITION", "ULTRON_CONDITION"),
        new("DRUUGE_DEFINED", 0, null, SiteKind.ArtifactRuins, SurfaceSheet.Ruins, "text.sphere", Extinct("DRUUGE_SHIP"),
            "ROSY_SPHERE", "ROSY_SPHERE_ON_SHIP"),
        new("PKUNK_DEFINED", 0, null, SiteKind.ArtifactRuins, SurfaceSheet.Ruins, "text.spindle", Extinct("PKUNK_SHIP"),
            "CLEAR_SPINDLE", "CLEAR_SPINDLE_ON_SHIP"),
        new("ANDROSYNTH_DEFINED", 1, null, SiteKind.Androsynth, SurfaceSheet.Ruins, AndrosynthReport, _ => true),
        new("URQUAN_WRECK_DEFINED", 6, null, SiteKind.Wreck, SurfaceSheet.UrquanWreck, "text.urquanwreck", _ => true,
            "PORTAL_KEY", "PORTAL_KEY_ON_SHIP"),
        new("SHIP_VAULT_DEFINED", 0, 0, SiteKind.Vault, SurfaceSheet.SyreenVault, VaultReport, _ => true),
    };

    /// <summary>The site on a world, if it's one of <see cref="Sites"/>.</summary>
    public static StorySite? SiteOf(string? special, WorldId world) => Sites.FirstOrDefault(s => s.On(special, world));

    /// <summary>genand.c's first visited ruin's bit: the ruins visited are kept above the nodes' own 16.</summary>
    public const int VisitedShift = 16;

    // The Androsynth's ruins visited (genand.c: the retrieved bits 16 to 31).
    private static int Visits(uint energyTaken) => System.Numerics.BitOperations.PopCount(energyTaken >> VisitedShift);

    /// <summary>
    /// Every lander report a story world can show (their string tables),
    /// with how many entries each has (the content's: a report that cycles
    /// goes round them; a test checks them against the content).
    /// </summary>
    public static readonly IReadOnlyDictionary<string, int> ReportEntries = new Dictionary<string, int>
    {
        [MoonbaseReport] = 1, ["text.maidens"] = 1, ["text.vuxbeast"] = 1, [UmgahCasterReport] = 2,
        ["text.burvixcaster"] = 1, ["text.utwigbomb"] = 1, ["text.taalodevice"] = 1, ["text.aquahelix"] = 1,
        ["text.sundevice"] = 1, ["text.eggcase"] = 1, [FwiffoReport] = 1,
        [RuinsReport] = 1, ["text.burvixeseruins"] = 1, ["text.ultron"] = 2, ["text.sphere"] = 2, ["text.spindle"] = 2,
        [AndrosynthReport] = 3, ["text.urquanwreck"] = 2, [VaultReport] = 3, [ChmmrBaseReport] = 1,
    };

    /// <summary>Fwiffo's report on Pluto (gensol.c), its place made as a ship goes into orbit; his pickup is stage 3c item 7's.</summary>
    public const string FwiffoReport = "text.fwiffo";

    /// <summary>Every lander report a story world can show (their string tables).</summary>
    public static IEnumerable<string> Reports => ReportEntries.Keys;

    // The node on this world of this scan, if it's one of Nodes.
    private static StoryNode? NodeOf(string? special, WorldId world, ScanType scan) =>
        Nodes.FirstOrDefault(n => n.Scan == scan && n.On(special, world));

    private static bool IsPluto(string? special, WorldId world) =>
        special == "SOL_DEFINED" && world.Planet == 8 && world.Moon is null;

    /// <summary>
    /// Whether the story has taken away the world's energy nodes, for the
    /// scan and the surface (its generator's generateEnergy giving none,
    /// the stage 3c plan's question 10, "the nodes on the scan following
    /// the story"): a single node's once it's been found (whoever took it,
    /// or a set-up's story field), and Pluto's once Fwiffo is found
    /// (FOUND_PLUTO_SPATHI, which has no retrieved bit). The egg cases have
    /// only their retrieved bits, as every other node.
    /// </summary>
    public static bool EnergyHidden(string? special, WorldId world, StoryState story) =>
        IsPluto(special, world) ? story.Get("FOUND_PLUTO_SPATHI") != 0
            : NodeOf(special, world, ScanType.Energy) is { } node && node.IsFound(story);

    /// <summary>
    /// The sheet a world's energy nodes are drawn and touched with on its
    /// surface (the generator's PlanetSideFrame[1]), or null if they aren't
    /// put there (Fwiffo's comes with stage 3c item 7). A single node's is
    /// there until it's been found (its retrieved bit says so too), an egg
    /// case's while its node isn't taken (<paramref name="energyTaken"/>,
    /// the world's retrieved energy nodes), a site's while it's open (a
    /// homeworld's ruins once its race is extinct), as the generators load it.
    /// </summary>
    public static SurfaceSheet? EnergySheet(string? special, WorldId world, StoryState? story, uint energyTaken = 0) =>
        story is null ? null
        : SiteOf(special, world) is { } site ? (site.Open(story) ? site.Sheet : null)
        : NodeOf(special, world, ScanType.Energy) is { } node && !node.IsFound(story)
          && (node.Found is not null || (energyTaken & 1u << node.Node) == 0) ? node.Sheet : null;

    /// <summary>
    /// The report a ship going into orbit around a world has its place in
    /// (the generator's generateOrbital loading its DiscoveryString), as
    /// tests/golden/story-nodes.json records: a single node's while it's
    /// not found, the egg case's while its node isn't taken
    /// (<paramref name="energyTaken"/>, the world's retrieved energy
    /// nodes), the Umgah broadcaster's first entry once the Spathi have
    /// shielded themselves and its second, which doesn't speak of the
    /// shield, while they haven't (genspa.c); Fwiffo's on Pluto until he's
    /// found (gensol.c; his pickup is stage 3c item 7's); a site's while
    /// it's open, at the entry the story says (<c>SiteEntry</c>: an
    /// artifact's or the wreck's second once it's found, the Androsynth's
    /// first chapter unread, the vault's by its state and the orbiting
    /// ship's own shuttle, <paramref name="held"/>). Null for any other world.
    /// </summary>
    public static ReportPlace? Discovery(string? special, WorldId world, StoryState story, uint energyTaken,
        Func<string, int>? held = null)
    {
        if (IsPluto(special, world))
            return story.Get("FOUND_PLUTO_SPATHI") == 0 ? new ReportPlace(world, FwiffoReport, 0) : null;
        if (SiteOf(special, world) is { } site)
            return site.Open(story) && SiteEntry(site, story, energyTaken, held ?? (_ => 0)) is int at
                ? new ReportPlace(world, site.Report, at) : null;
        var node = Nodes.FirstOrDefault(n => n.On(special, world));
        if (node is null || node.IsFound(story) || (node.Found is null && (energyTaken & 1u << node.Node) != 0))
            return null;
        int entry = node.Report == UmgahCasterReport && story.Get("SPATHI_SHIELDED_SELVES") == 0 ? 1 : 0;
        return new ReportPlace(world, node.Report, entry);
    }

    // The entry of a site's text the story says now (its generateOrbital's
    // SetAbsStringTableIndex), or null for none (the Androsynth's chapters all read).
    private static int? SiteEntry(StorySite site, StoryState story, uint energyTaken, Func<string, int> held) =>
        site.Kind switch
        {
            // gensup.c, gendru.c, genpku.c, genwreck.c: already picked up, skip the first report.
            SiteKind.ArtifactRuins or SiteKind.Wreck => story.Get(site.Found!) != 0 ? 1 : 0,
            // genand.c: advanced to the first chapter unread, none once they're all read.
            SiteKind.Androsynth => Visits(energyTaken) < ReportEntries[site.Report] ? Visits(energyTaken) : null,
            // genvault.c:69: opened; the shuttle aboard (the ship's own, the plan's readings of question 5); locked.
            SiteKind.Vault => story.Get("SHIP_VAULT_UNLOCKED") != 0 ? 2 : held("SYREEN_SHUTTLE_ON_SHIP") != 0 ? 1 : 0,
            _ => 0,
        };

    /// <summary>
    /// The world's pickupEnergy as a lander touches an energy node: a single
    /// node's report is shown and the lander goes up (GenerateDefault_landerReport,
    /// SetLanderTakeoff), it's found (its story field), its device is the
    /// finder's (a held one, <see cref="Finder.Give"/>; the shared Lunar
    /// base, the group's), and the generator's other fields set. True if
    /// the node is taken; false for a node that stays (a <see cref="Sites"/>
    /// world's: <see cref="PickupSite"/>), whose report and take-off
    /// <paramref name="finder"/> carries, and for any other node, changing
    /// nothing.
    /// </summary>
    public static bool PickupEnergy(string? special, WorldId world, StoryState story, Finder finder, int node)
    {
        if (SiteOf(special, world) is { } site)
        {
            PickupSite(site, story, finder, node);
            return false;   // do not remove the node
        }
        return NodeOf(special, world, ScanType.Energy) is { } n && n.Node == node && !n.IsFound(story) && Take(n, story, finder);
    }

    /// <summary>
    /// A site's pickupEnergy, the node staying (stage 3c item 6), with its
    /// text read from the shared story as it's touched (<see cref="Finder.ReadAt"/>,
    /// the plan's question 10): a homeworld's ruins show their report once
    /// a visit (GenerateDefault_landerReportCycle); an artifact's ruins
    /// (gensup.c, gendru.c, genpku.c) and the wreck (genwreck.c) the next
    /// entry, and the first time, the lander up, the shared record set and
    /// the device the finder's ship's; the Androsynth's (genand.c) a chapter
    /// at a ruin not visited before, in order, the ruin marked visited
    /// (<see cref="Finder.Visit"/>) and the chapter the group's story
    /// (<see cref="Finder.TellEveryone"/>); the vault (genvault.c), by the
    /// finder's ship's shuttle: opened, its last text; with the shuttle, its
    /// text, the lander up, the vault opened, the shuttle used up and the
    /// Syreen's visits counted afresh; locked, its first text once
    /// (GenerateDefault_landerReport) and the vault known.
    /// </summary>
    public static void PickupSite(StorySite site, StoryState story, Finder finder, int node)
    {
        switch (site.Kind)
        {
            case SiteKind.Ruins:
                finder.LanderReportCycle();
                break;
            case SiteKind.ArtifactRuins:
            case SiteKind.Wreck:
            {
                bool found = story.Get(site.Found!) != 0;
                finder.ReadAt(found ? 1 : 0);
                finder.LanderReportCycle();
                if (found)
                    break;
                // Just picked it up (the artifact can be picked up from any ruin).
                finder.SetTakeoff();
                story.Set(site.Found!, 1);
                finder.Give(site.Device!);
                break;
            }
            case SiteKind.Androsynth:
            {
                int bit = node + VisitedShift;
                if ((finder.EnergyTaken & 1u << bit) != 0)
                    break;   // already visited this ruin
                finder.Visit(bit);
                finder.ReadAt(Visits(finder.EnergyTaken));
                finder.LanderReportCycle();
                finder.TellEveryone();
                break;
            }
            case SiteKind.Vault:
                if (story.Get("SHIP_VAULT_UNLOCKED") != 0)
                {
                    finder.ReadAt(2);
                    finder.LanderReportCycle();   // the final report, "omg empty" and whatnot
                }
                else if (finder.Held("SYREEN_SHUTTLE_ON_SHIP") != 0)
                {
                    finder.ReadAt(1);
                    finder.LanderReportCycle();
                    finder.SetTakeoff();
                    story.Set("SHIP_VAULT_UNLOCKED", 1);
                    finder.Take("SYREEN_SHUTTLE_ON_SHIP");
                    story.Set("SYREEN_HOME_VISITS", 0);
                }
                else
                {
                    finder.ReadAt(0);
                    finder.LanderReport();
                    if (story.Get("KNOW_SYREEN_VAULT") == 0)
                        story.Set("KNOW_SYREEN_VAULT", 1);
                }
                break;
        }
    }

    /// <summary>
    /// The report a world shows as a ship comes to it, the ship not put in
    /// orbit (generateOrbital's own DoDiscoveryReport): Procyon's starbase,
    /// the Chmmr's moon (genchmmr.c), whatever the story. Null for any other.
    /// </summary>
    public static LanderReport? ArrivalReport(string? special, WorldId world) =>
        special == "CHMMR_DEFINED" && world.Planet == 1 && world.Moon == 0 ? new LanderReport(ChmmrBaseReport) : null;

    /// <summary>
    /// The world's pickupLife's story as the lander takes a creature (or its
    /// bolt does, before the node is marked retrieved): Zex's Beauty
    /// (genvux.c, the first node on its world) shows its report, sends the
    /// lander up and is the finder's; any other creature is the default's,
    /// taken with nothing more (the Evil Ones' story is <see cref="PickupLife"/>'s).
    /// </summary>
    public static void PickupCreature(string? special, WorldId world, StoryState story, Finder finder, int node)
    {
        if (NodeOf(special, world, ScanType.Biological) is { } n && n.Node == node && !n.IsFound(story))
            Take(n, story, finder);
    }

    // A single node taken, as its generator's pickup has it: the report, the
    // take-off, then its story fields in order (the found field, the device,
    // the others).
    private static bool Take(StoryNode n, StoryState story, Finder finder)
    {
        finder.LanderReport();
        finder.SetTakeoff();
        if (n.Found is { } found)
            story.Set(found, 1);
        if (DeviceTable.HeldByShips(n.Device))
            finder.Give(n.Device);
        else
            story.Set(n.Device, 1);
        foreach (var (field, value) in n.Also)
            story.Set(field, value);
        return true;
    }

    /// <summary>NUM_EVIL_ONES (genspa.c): the Evil Ones on Spathiwa.</summary>
    public const int EvilOnes = 32;

    private static bool IsSpathiwa(string? special, WorldId world) =>
        special == "SPATHI_DEFINED" && world.Planet == 0 && world.Moon is null;

    /// <summary>
    /// GenerateSpathi_pickupLife for Spathiwa, as a creature there is taken
    /// (before its node is marked retrieved; <paramref name="retrievedBefore"/>
    /// is countNodesRetrieved's count until then): SPATHI_CREATURES_EXAMINED,
    /// and SPATHI_CREATURES_ELIMINATED with the last of the 32 Evil Ones.
    /// Both are shared story fields, as the world's taken nodes are, so
    /// whichever players take them, the last one taken eliminates them.
    /// Other worlds' life is the default generator's: nothing more.
    /// </summary>
    public static void PickupLife(string? special, WorldId world, StoryState story, int retrievedBefore)
    {
        if (!IsSpathiwa(special, world))
            return;
        story.Set("SPATHI_CREATURES_EXAMINED", 1);
        if (retrievedBefore + 1 == EvilOnes)
            story.Set("SPATHI_CREATURES_ELIMINATED", 1);   // the last creature picked up
    }

    /// <summary>RAINBOW_DEFINED: the special generator of the stars whose first planet is a rainbow world.</summary>
    public const string RainbowSpecial = "RAINBOW_DEFINED";

    /// <summary>
    /// The rainbow world's bit in RAINBOW_WORLD0/1 for a star
    /// (GenerateRainbowWorld_generateOrbital's which_rainbow): the number
    /// of RAINBOW_DEFINED stars before it in the map's order; null for a
    /// star that isn't one.
    /// </summary>
    public static int? RainbowIndex(StarMap map, int star) =>
        map.Stars[star].Special == RainbowSpecial
            ? map.Stars.Take(star).Count(s => s.Special == RainbowSpecial)
            : null;

    /// <summary>
    /// A ship goes into orbit around a world (the generator's
    /// generateOrbital, as UQM's orbit runs it, a saved game loaded in
    /// orbit too): around a rainbow world (planet 0 of a RAINBOW_DEFINED
    /// star) its bit is set in RAINBOW_WORLD0/1 (genrain.c), the group's
    /// one story field, so whoever orbits it, the group has found it, and
    /// the Melnorme pay for it once (the plan's question 6); around a
    /// shattered world (planet 0 of an egg case's system, genmyc.c) the
    /// players have seen one (KNOW_ABOUT_SHATTERED 1 from 0), which the
    /// Mycon can then be asked about.
    /// </summary>
    public static void Orbited(StarMap map, WorldId world, StoryState story)
    {
        if (world.Planet != 0 || world.Moon is not null)
            return;
        // genmyc.c:164-168 (GenerateMycon_generateOrbital): the first
        // planet of an egg case's system is a shattered world; seeing one
        // lets the players ask the Mycon about them (KNOW_ABOUT_SHATTERED
        // 1, unless they know more already: 2 once told), the group's, as
        // every story field (the stage 3b-2 plan's question 2's kind of
        // pickup: no device, read by a talk).
        if (map.Stars[world.Star].Special is "EGG_CASE0_DEFINED" or "EGG_CASE1_DEFINED" or "EGG_CASE2_DEFINED"
            && story.Get("KNOW_ABOUT_SHATTERED") == 0)
            story.Set("KNOW_ABOUT_SHATTERED", 1);
        if (RainbowIndex(map, world.Star) is not int which)
            return;
        int mask = story.Get("RAINBOW_WORLD0") | (story.Get("RAINBOW_WORLD1") << 8);
        mask |= 1 << which;
        story.Set("RAINBOW_WORLD0", mask & 0xFF);
        story.Set("RAINBOW_WORLD1", mask >> 8);
    }
}
