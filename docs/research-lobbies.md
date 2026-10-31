# Research: how open-source games do lobbies and connections

How free and open-source multiplayer games let players find a host and connect through home routers, and what that means for Crateris. Researched 2026-10-31 from project docs and source; details for a few projects come from older docs and may have changed.

## The problem

A guest has to reach the host's game on a home connection. Three separate jobs are involved:

1. **Finding the game:** a server list, a lobby with chat, or a code shared between friends.
2. **Getting through the host's router:** port forwarding (manual or UPnP), NAT hole punching (STUN), or a relay that both sides connect out to (TURN).
3. **Paying for it:** anything beyond direct connections needs a server someone runs all the time.

## What other projects do

| Project | Finding games | Getting through routers | Who runs what |
| --- | --- | --- | --- |
| **OpenRA** (C#, GPLv3) | Public server browser fed by a master server. A host ticks "Advertise online" (`Server.AdvertiseOnline`); the master server checks the game can be reached before listing it. Direct IP also works | UPnP port forwarding by default (can be turned off), else manual forwarding of TCP 1234. No hole punching or relay | Project runs a small master server ([OpenRAMasterServer](https://github.com/OpenRA/OpenRAMasterServer), scripts only) |
| **OpenTTD** (C++, GPLv2) | Public list, plus **invite codes** (about 7 characters) from its Game Coordinator. Servers are invite-only or public | Coordinator tries in order: direct IPv4/IPv6, then STUN hole punching, then a **TURN relay** as last resort | openttd.org runs the coordinator, STUN (`stun.openttd.org`) and TURN relay. The relay sees all traffic, so users must trust it |
| **Battle for Wesnoth** (C++, GPLv2+) | Central lobby server (`wesnothd`) with game list, player list and chat. Players can also host their own `wesnothd` for LAN | The lobby server itself carries game traffic, so hosts need no port forwarding | Project runs `server.wesnoth.org`; third parties run alternates |
| **0 A.D.** (C++, GPLv2+) | Lobby on an XMPP chat server (ejabberd) with a bot that keeps the game list | STUN on the lobby server (UDP 3478) so hosts don't need to forward ports. Earlier UPnP was dropped as unreliable. Uses ENet, IPv4 only | Project runs the XMPP server, bots and STUN |
| **SuperTuxKart** (C++, GPLv3) | Server list from the STK online API; needs an STK account to list a public server | STUN; otherwise firewall changes or port forwarding | Project runs the API and STUN |
| **Zero-K** (Lua/C#, GPL) | Own lobby server (`ZkLobbyServer`, C#) and in-game lobby client (Chobby). Free on Steam since 2018; all downloads play together | Steam co-op through a **separate launcher program**: `SteamP2PPortProxy` forwards a local UDP port over Steam's peer-to-peer network, so the GPL game engine never links Steam's SDK. Also has STUN code | Project runs the lobby, website and autohost servers |
| **Luanti** (Minetest, C++, LGPL) | Public server list; a server sets `server_announce = true` | Server host forwards the port; no traversal | Project runs the list server |
| **DDNet / Teeworlds** (C++, zlib-like) | Game servers register with a master server over HTTP; the master proves the server listens on its port, then publishes `servers.json` | Dedicated servers on public addresses | Community runs several master servers |
| **Mindustry** (Java, GPLv3) | Direct IP, LAN discovery, and a community server list kept as a JSON file in the GitHub repo (`servers_v7.json`), changed by pull request | Server hosts forward ports | No list server at all: GitHub hosts the list. Also on Steam |

Godot-specific: **[noray](https://github.com/foxssake/noray)** (MIT) is an open-source connection broker and relay used with Godot through the [netfox.noray](https://godotengine.org/asset-library/asset/9845) addon (MIT, GDScript). Players register and get IDs; the broker arranges NAT hole punching for a UDP (ENet) connection and falls back to relaying. It ships as a Docker image to run on any small server.

Platform services: **Steam** and **Epic Online Services (EOS)** both offer lobbies, peer-to-peer connections, NAT traversal and an automatic relay on the company's servers, at no cost to the game. Both SDKs are closed source.

| | Steam | Epic Online Services |
| --- | --- | --- |
| Game must be on the store | Yes (app ID, $100 Steam Direct fee) | No; any game can use it |
| Player needs an account | Yes, plus Steam running | Not for lobbies and P2P if Device ID login works for them (one guide says so; not yet checked in Epic's docs). Friend invites through the overlay need Epic accounts |
| Friend invites | Through the Steam friends list and overlay | Through the Epic overlay (needs accounts), or share the lobby ID as a code |
| Relay when direct fails | Yes (Valve's network) | Yes (EOS tries NAT punch-through, then relays) |
| GPL | Keep the SDK in a separate bridge program (Zero-K) | Same |

## Patterns

1. **Direct IP + UPnP** (OpenRA, Luanti, Mindustry): no servers to run. Fails when UPnP is off and the host can't forward ports.
2. **Public list server** (OpenRA, Luanti, DDNet): a tiny service hosts register with. Helps strangers find games; friends don't need it. Mindustry gets away with a JSON file on GitHub.
3. **Broker with codes, hole punching and relay** (OpenTTD; noray for Godot): best reach, since almost everyone can connect. Needs an always-on server, and the relay uses bandwidth.
4. **Lobby server that also carries traffic** (Wesnoth): simple for turn-based games, too slow and expensive for real-time combat.
5. **Steam as an add-on** (Zero-K, Mindustry, OpenTTD and Wesnoth on the store): the game stays playable without Steam. Zero-K keeps Steam's SDK out of the GPL engine by putting it in a separate program that only forwards network traffic.

## What this means for Crateris

Crateris is for friends, up to 4 players, real-time combat, and the rule is "simple and cheap".

- **Stage 1: direct connection with UPnP.** The host's game asks the router to open the port with Godot's built-in `UPNP` class, as OpenRA does, and shows the address to share. Guests type it in. Document Tailscale or ZeroTier for routers where UPnP fails. Cost: nothing.
- **Later, if friends often can't connect: invite codes through noray.** Self-host noray on a small server (a few dollars a month) for OpenTTD-style codes, hole punching and a relay fallback. The netfox.noray addon is GDScript; its protocol is small enough to call from C# or port. This is the main upgrade path and needs no change to the game's networking, which stays Godot ENet.
- **No public server list.** Friends share an address or a code. If a list is ever wanted, a JSON file in the repo (Mindustry's approach) costs nothing.
- **Steam, if ever wanted: follow Zero-K.** The free download and store page don't conflict with the GPL, as OpenTTD, Wesnoth and Zero-K show. For Steam friends and invites, put Steam's SDK in a small separate bridge program written fresh for Crateris (so the project owns all of it and can license it without GPL conflicts), which forwards the game's local UDP port over Steam's peer-to-peer network. The game itself never links Steam. Check with a lawyer before shipping this.
- **Epic Online Services is the better platform option.** The same bridge works, and EOS needs no store listing and possibly no player account, so a host could share an EOS lobby ID as an invite code with no server of ours. Confirm Device ID login for lobbies on PC in Epic's docs first.

## Sources

- OpenRA: [create-server screen text (UPnP, advertise online)](https://github.com/pchote/OpenRA/blob/37e03162a9952efe5b00bcee1f27e145517ae4d3/mods/common/chrome/multiplayer-createserver.yaml), [FreeBSD man page (settings)](https://man.freebsd.org/cgi/man.cgi?manpath=freebsd-ports&query=openra&sektion=6), [forum: manual forwarding](https://forum.openra.net/viewtopic.php?p=283672), [OpenRAMasterServer](https://github.com/OpenRA/OpenRAMasterServer)
- OpenTTD: [Game Coordinator docs](https://github.com/OpenTTD/OpenTTD/blob/master/docs/game_coordinator.md)
- Wesnoth: [Multiplayer servers wiki](https://wiki.wesnoth.org/MultiplayerServers), [wesnothd man page](https://man.archlinux.org/man/wesnothd.6.en_GB)
- 0 A.D.: [lobby bots (setup docs)](https://github.com/rossenburgg/lobby-bots), [Trac #2305: NAT traversal](https://trac.wildfiregames.com/ticket/2305)
- SuperTuxKart: [NETWORKING.md](https://sources.debian.org/src/supertuxkart/1.5-2/NETWORKING.md)
- Zero-K: [Zero-K-Infrastructure (GPL-3.0)](https://github.com/ZeroK-RTS/Zero-K-Infrastructure), [SteamP2PPortProxy.cs](https://github.com/ZeroK-RTS/Zero-K-Infrastructure/blob/master/ChobbyLauncher/SteamP2PPortProxy.cs), [Spring wiki: lobby development](https://www.springrts.com/wiki/Lobby_Development), [Zero-K coming to Steam (GamingOnLinux)](https://www.gamingonlinux.com/2017/11/open-source-rts-zero-k-is-coming-to-steam-with-a-campaign-being-worked-on/page=1/)
- Luanti: [setting up a server](https://docs.luanti.org/for-server-hosts/setup/), [finding servers](https://docs.luanti.org/for-players/servers/)
- DDNet: [docs](https://codedoc.ddnet.tw), [Teeworlds master server notes](https://influck.notion.site/teeworlds-master-servers-ffd3a83022e345e6a0eb8303accba440)
- Mindustry: [servers_v7.json](https://raw.githubusercontent.com/Anuken/Mindustry/master/servers_v7.json), [SERVERLIST.md](https://github.imc.re/Anuken/Mindustry/blob/master/SERVERLIST.md)
- Epic Online Services: [coherence docs: EOS relay](https://docs.coherence.io/hosting/client-hosting/implementing-client-hosting/epic-online-services-eos-relay), [Unreal forum: EOS P2P NAT traversal and relay](https://forums.unrealengine.com/t/question-about-networking-ports-and-eos/598259), [NetickEOSTransport (Device ID login)](https://github.com/unitycoder/NetickEOSTransport)
- noray: [repository](https://github.com/foxssake/noray), [netfox.noray addon](https://godotengine.org/asset-library/asset/9845)
