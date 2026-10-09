# Slime abilities

Checked by static analysis of the components named below and of SlimeSubbehaviourPlexer and
PhysicsUtil, and by reading every slime prefab and its appearance's extras (InstalledAbilityTests,
AbilityTests). openranch: src/OpenRanch.Simulation/Abilities.cs and Blast.cs (code-only numbers),
game/scripts/Slimes/AbilityBehaviours.cs (the pieces), checked by `--slime-zoo --zoo-part abilities`.

## How abilities fit in

- Each ability is a component on the slime prefab. A largo's prefab carries both parents' components
  (already combined in the data), so a largo has both abilities.
- Abilities that compete for the slime's attention are sub-behaviours: on each rethink the plexer asks
  every sub-behaviour, in the prefab's component order, how relevant it is (0-1); only a strictly higher
  value takes the lead, so ties go to the earlier component. Some block rethinking while they run.
- Being calmed by water spray pauses most of these timers; water isn't modelled yet.
- Some prefabs needed for effects hang off the slime's appearance (`SlimeAppearance.CrystalAppearance`,
  `TornadoAppearance`, `GlintAppearance`, `VineAppearance`); a largo's appearance takes each extra from
  whichever parent has it.

## Explosions

Used by boom slimes, boom gordos and feral stomps (PhysicsUtil.Explode). Every body touching the
radius gets a one-off force away from the centre of power × (1 − max(2, distance) ÷ radius)². A player
in reach takes damage from the maximum at the centre to the minimum at the edge (rounded half to even)
and is pushed with power × (1 − distance ÷ radius) × 0.001.

## Boom (`BoomSlimeExplode`)

- Data: `explodePower`, `explodeRadius`, `minPlayerDamage`, `maxPlayerDamage`.
- Delay between explosions: from 45 s (calm) to 10 s (agitated), ±0.1 calmness jitter. The first wait
  is a random 25-100% of one delay. When due it matters 1.
- It grimaces for 1.5 s, explodes, then is frazzled 5 s; it can't rethink until that is over.

## Rad aura (`RadSource`, `RadSlimeExpand`)

- The aura is a child trigger sphere with `RadSource.radPerSecond`. A player inside soaks up that much
  radiation per second; over the maximum (100 at the start) radiation turns into health loss in steps of 10.
- Every 180 s (calm) to 30 s (agitated) expanding matters 1: the aura swells to 1.5× over 3 s (no
  rethinking), stays 10 s, then shrinks. Between 1× and 1.5× the size changes 1/3 per second; anything
  below normal size (calmed by water) changes 5 per second.

## Crystal (`CrystalSlimeLaunch`, `CrystalSpikesLifecycle`)

- Standing, and its timer up (0.25 game hours calm to 0.05 agitated), launching matters 0.3. It takes
  its flat right-hand axis to roll about, curls up for a game minute, then launches: a one-step force of
  200 up (not scaled by mass) plus 40 × mass forward, and rolls (torque 1200 × mass) for at least
  6 game seconds.
- Spikes: one big spike where it was, then 0.2 s later a ring of small ones 1.5 m out, ⌈4 × mass⌉ up to
  (not including) ⌈7 × mass⌉ of them, evenly spaced from a random start angle, each on the ground found
  within 2 m below its spot, at a random turn.
- A spike (prefab data) hurts the player who touches it by `damagePerHit` and crumbles after
  `lifetime` game hours.
- Rock, crystal and quicksilver slimes (and their largos) also hurt the player on any touch
  (`DamagePlayerOnTouch.damagePerTouch` at most every `repeatTime` s, after a 0.1 s amnesty).

## Quantum (`GenerateQuantumQubit`, `QuantumSlimeSuperposition`, `QuantumVibration`)

- Data: `QubitSearchRadius`, `MaxQubits`, `Min/MaxGenerationDelay`, `Min/MaxSuperposeDelay`,
  `AgitationCutoff`.
- It vibrates while agitation is over the cutoff (strength rising to 1 at full agitation).
- Qubits: ghost copies placed on clear ground (nothing within 0.6 m, not inside a corral) at random
  points within the search radius, 0.61 m up; up to five tries; past the maximum the oldest fades.
  Delay between generations: straight from max (calm) to min (agitated).
- Jumping (superposition) matters 1 when it vibrates, its timer is up, all its qubits have arrived and it
  isn't held: it moves into a random clear qubit and the rest fade. The next jump waits max (calm) to
  min (agitated) seconds; the first waits the maximum.

## Dervish (`DervishSlimeSpin`)

- A stronger hover: lift 600 × (1 − height ÷ 5 m) per unit mass (a largo: up to 9 m), with the hover's
  timing (10-25 s), drift and 6 s length. A mini tornado spins under it while it spins.
- At 0.95 agitation or more each spin lets a whirlwind loose (`TornadoAppearance.fullWhirlwindPrefab`):
  its trigger capsules carry things up to its `ActorVortexer.tornadoHeight` and throw them out; it is
  gone after `DestroyAfterTime.lifeTimeHours`. At most six per slime; a seventh ends the oldest.

## Tangle (`GroundVine`, `PollenCloudController`)

- Vines: standing, `cooldown` seconds after the last, the food it wants most within the vine's
  `maxSearchRad` matters drive² × 0.95 (like going for food; the vine comes earlier on the prefab, so it
  wins the tie). A vine grows under the food (3-4 m above it, 0.75 s per 4 m), lifts it, brings it
  2-2.5 m up in front of the slime's mouth and lets the slime eat it.
- Pollen: above `startGrowthAgitation` a cloud grows on it at `pctGrowthPerGameHour` toward a size set
  by how far past that agitation it is (up to `maxCloudScale`). At 95% the cloud actor is let go,
  drifting forward 1 m/s, and lasts its `PollenCloudDestructor.gameHrsToLive`.

## Hunter cloaking (`SlimeStealth`)

- Invisible for 5 s after appearing; then seen, except while stalking (StalkConsumable switches stealth
  on when chosen and off when it stops). Opacity moves 2 per second; always seen while held.

## Mosaic glints (`GlintController`)

- Every 10 game minutes it checks; a glint appears every 0.5 game hours × (1 − 0.8 × agitation),
  within 7.5 m (calm) to 30 m (agitated) of it, never below it. Glints go through suspended, ready
  and free phases (1, 0.5 and 0.5 game hours, the first two shortened by agitation); free glints fall and
  burst (`ExplodeOnTouching`, no damage, ignites).

## Not done yet (openranch)

- Effects, sounds and faces; calming by water; the mini tornado's vortex; the whirlwind's real carry and
  throw; qubits travelling out and their appearance; glints' phases (openranch keeps one glint for all
  three) and bursting; pollen's effects; the vine's real path (openranch drops the food on the slime).
