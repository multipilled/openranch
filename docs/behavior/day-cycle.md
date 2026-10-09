# The day cycle: world clock, days and sleeping

How the game counts time, which day it is, and what sleeping does. For how the light and fog follow
the hour, see [day-and-night.md](day-and-night.md).

Code: `src/OpenRanch.Ranch/WorldClock.cs` (`WorldClock`, `DayCycle`, `DayLength`).

## World time

- The whole world runs on one clock, **world time**: game seconds since midnight before day 1. A
  game day is 86,400 game seconds and an hour 3,600. Saves store only this number (the world block's
  `worldTime`); the day and the hour shown are worked out from it. Timers on slimes, crops, feeders,
  gadgets and so on are world times too.
- **The day number** shown on the clock counts from 1: day 1 is world time 0 up to 86,400, day 2
  the next 86,400, and so on.
- **The time of day** is world time within the current day. The clock shows hours and minutes,
  rounded down to the minute.
- **A new game** starts at 9:00 on day 1, world time 32,400.

Checked: day length and storage read from the original's saves (`docs/formats/save-files.md`); the
day number and the 9:00 start come from static analysis of the time director (`TimeDirector`) and
are code-only facts (`WorldClock.NewGameStart`). All 15 version 12 saves on the development PC have
world times past 9:00 on day 1, as they should (`Every_supported_save_imports_and_round_trips`).

## How fast the clock runs

- In normal play a game day lasts a fixed number of real seconds, read from the install: the time
  director script (`TimeDirector`) holds `secsPerGameDay`. While sleeping the clock runs much faster,
  at `ffSecsPerGameDay` real seconds per game day.
- The clock stops while the game is paused (menus, the ranch house screen except while sleeping)
  and does not run in the main menu.

Checked: both values read from the install by `DayLength.Read`; the install test prints them. The
sleeping value in the install differs from the script's built-in default, so it must be read, not
assumed. The pausing rules come from static analysis of the time director.

In the install a game day lasts 1,440 real seconds (24 minutes), so a game minute passes every real
second; sleeping runs at 4 real seconds per game day.

openranch runs the clock in The Ranch with `game/scripts/World/WorldTime.cs`: it moves the ranch's world
time (the save's, or a new game's 9:00 on day 1) with `DayCycle` every frame, stops while the game is
paused, and shows "Day N, HH:MM" at the top right (openranch's own readout; the original's HUD clock is
not drawn yet). Testing options: `--hour H` starts at hour H of the starting day, `--day-speed X` runs
the clock X times faster, and `--day-check` checks the start time, the pace (normal and 60 times faster),
the lighting at four hours and the timed objects, then quits.

## Parts of the day

The clock's icon splits the day into four parts (code-only, from static analysis of the time
director's icon choice; `WorldClock.NightEnds` and so on):

| Part | From | To |
| --- | --- | --- |
| Night | 19:12 | 4:48 |
| Dawn | 4:48 | 7:12 |
| Day | 7:12 | 16:48 |
| Dusk | 16:48 | 19:12 |

The boundaries are fractions of the day (0.2, 0.3, 0.7 and 0.8). These are for the icon only;
lighting has its own blend, described in day-and-night.md.

## Sleeping

- Sleeping in the ranch house runs the clock fast until the next **6:00**: later the same day if it
  is before 6:00, otherwise 6:00 the next morning. Going to sleep at exactly 6:00 sleeps a whole day.
- The wake-up time is rounded to the nearest whole game second. The clock stops exactly there.
- The world keeps running while the clock runs fast: slimes get hungry, crops grow, timers fire.
  Nothing about sleeping is saved; a save holds only the world time.

Checked: static analysis of the ranch house's sleep button and the time director (code-only).
openranch's `DayCycle` does the same and is unit tested.

## Ranch areas the player is away from

Each part of the ranch (the main ranch and each expansion) is only simulated in full while the
player is near it. When the player leaves one, the game notes the world time; when they come back,
the area catches up on what happened meanwhile, chiefly feeding: auto-feeders, food lying in corrals
and water sources are handed out to the slimes there as if time had passed. The noted times are
stored in the ranch block's `ranchFastForward`, by area id (openranch: `RanchState.AreaCatchUpTimes`).

Checked: the stored times read from the development PC's saves. How the catch-up shares out the food
is not modelled yet.

## Not modelled yet

- Death costs time: the clock jumps forward by a number of game hours, or to the dawn after the next
  dusk, as the game mode's settings say, and always by at least 390 game seconds.
- Day-based events (the market's daily prices, mail arriving, the Range Exchange's daily offers)
  hang off the clock and belong to their own notes.
