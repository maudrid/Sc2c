/*
 * Attack on Crateris: the rest of UQM that outfit.c, shipyard.c, sis.c and
 * menu.c call, for the outfitting recording program (golden/outfit.c).
 * They only draw, flash, load or wait, or serve the input loop, none of
 * which the recording drives: each does nothing and gives 0 (the fleet's
 * queues are UQM's own build.c and displist.c, linked). Declared without UQM's headers (each is called
 * through its own prototype; 0 is a null pointer, FALSE or 0 for all).
 *
 * Copyright (C) 2026 Attack on Crateris contributors.
 *
 * This program is free software; you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation; either version 2 of the License, or
 * (at your option) any later version.
 */

#define NOTHING(name) long name (void) { return 0; }

NOTHING (Alarm_addAbsoluteMs)
NOTHING (Alarm_remove)
NOTHING (AnyButtonPress)
NOTHING (CaptureDrawable)
NOTHING (CaptureStringTable)
NOTHING (DestroyDrawable)
NOTHING (DestroyStringTable)
NOTHING (DoShipSpin)
NOTHING (DrawRectangle)
NOTHING (FindMasterShipIndex)
NOTHING (Flash_createHighlight)
NOTHING (Flash_nextTime)
NOTHING (Flash_postUpdate)
NOTHING (Flash_preUpdate)
NOTHING (Flash_process)
NOTHING (Flash_setFrameTime)
NOTHING (Flash_setMergeFactors)
NOTHING (Flash_setRect)
NOTHING (Flash_setSpeed)
NOTHING (Flash_start)
NOTHING (Flash_terminate)
NOTHING (FlushInput)
NOTHING (GetClusterName)
NOTHING (GetColorMapAddress)
NOTHING (GetContextFontLeading)
NOTHING (GetStringAddress)
NOTHING (GetTimeCounter)
NOTHING (LoadStringTableInstance)
NOTHING (PlayFrame)
NOTHING (ReleaseDrawable)
NOTHING (ReleaseStringTable)
NOTHING (SetAbsStringTableIndex)
NOTHING (SetColorMap)
NOTHING (SetInputCallback)
NOTHING (SetRelStringTableIndex)
NOTHING (SleepThreadUntil)
NOTHING (TextRect)
NOTHING (inHyperSpace)
NOTHING (log_add)
NOTHING (skipUTF8Chars)
NOTHING (utf8StringCopy)

int ScreenWidth = 320, ScreenHeight = 240;
void *CurStarDescPtr;
