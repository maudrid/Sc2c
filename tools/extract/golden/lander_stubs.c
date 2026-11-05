/*
 * Attack on Crateris: do-nothing stand-ins for the parts of UQM that
 * lander.c links but doesn't need to move the lander: drawing, sound,
 * fonts, the scan map, ship queues and the game's resources.
 *
 * Copyright (C) 2026 Attack on Crateris contributors.
 *
 * This program is free software; you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation; either version 2 of the License, or
 * (at your option) any later version.
 *
 * Compiled without UQM's headers, so each stand-in can take any
 * arguments; functions return 0 and the data are zeroed space.
 */

long BatchGraphics () { return 0; }
long CaptureDrawable () { return 0; }
long CaptureStringTable () { return 0; }
long CloneShipFragment () { return 0; }
long CreateContextAux () { return 0; }
long DestroyContext () { return 0; }
long DestroyDrawable () { return 0; }
long DestroyMusic () { return 0; }
long DestroySound () { return 0; }
long DestroyStringTable () { return 0; }
long DrawLanders () { return 0; }
long DrawPlanetSurfaceBorder () { return 0; }
long DrawStarBackGround () { return 0; }
long DrawStorageBays () { return 0; }
long FindMasterShip () { return 0; }
long GetContextClipRect () { return 0; }
long GetFrameRect () { return 0; }
long InitGroupInfo () { return 0; }
long InitPlanetInfo () { return 0; }
long InitSISContexts () { return 0; }
long LoadGraphicInstance () { return 0; }
long LoadSoundInstance () { return 0; }
long NotPositional () { return 0; }
long PlaySound () { return 0; }
long ReleaseDrawable () { return 0; }
long ReleaseStringTable () { return 0; }
long RepairBackRect () { return 0; }
long RotatePlanetSphere () { return 0; }
long ScreenTransition () { return 0; }
long SetContextClipRect () { return 0; }
long SetContextFGFrame () { return 0; }
long SetContextFont () { return 0; }
long SetMenuSounds () { return 0; }
long SetRaceAllied () { return 0; }
long SetTransitionSource () { return 0; }
long UninitGroupInfo () { return 0; }
long UninitPlanetInfo () { return 0; }
long free_ship () { return 0; }
long load_orbit_theme () { return 0; }
long load_ship () { return 0; }
long log_add () { return 0; }
long utf8StringCopy () { return 0; }
long utf8StringCountN () { return 0; }

char CurStarDescPtr[64], FlagStatFrame[64], FontGradFrame[64],
		PlanetContext[64], Screen[64], TinyFont[64], master_q[256];
