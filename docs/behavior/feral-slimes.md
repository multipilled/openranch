# Feral slimes (and slimes that go for the player)

Checked by static analysis of SlimeFeral, FeralizeOnLargoTransformed, GotoPlayer, AttackPlayer,
Chomper, FeralSlimeButtstomp, SlimeEat and DirectedActorSpawner, and by reading every slime prefab
(InstalledAbilityTests). openranch: src/OpenRanch.Simulation/Feral.cs, game/scripts/Slimes/FeralBehaviours.cs.
Checked by `--slime-zoo --zoo-part feral` (FeralTrial) and FeralTests.

## Who can be feral

- Every slime prefab carries `SlimeFeral`, but on a slime of normal vacuum size it removes itself at
  once: only largos (vacuum size large) can be feral. A feral spawner asking for a normal slime just
  logs a warning.
- Every largo prefab also carries `GotoPlayer` and `AttackPlayer` (both switched off in the data),
  `Chomper` and `FeralSlimeButtstomp`. Tarrs carry `GotoPlayer` and `AttackPlayer` switched on.

## Data

- `SlimeFeral.dynamicToFeral`: turns feral by itself when agitation reaches 0.999 (hunters only).
  `dynamicFromFeral`: eating calms it (stops being feral). `feralLifetimeHours`: game hours until it poofs.
- `FeralizeOnLargoTransformed` (hunter slimes, so hunter largos): the largo is feral the moment it forms.
- `FeralSlimeButtstomp.explodePower`, `explodeRadius`, `minPlayerDamage`, `maxPlayerDamage`.
- `AttackPlayer.damagePerAttack`; `Chomper.timePerAttack` (seconds after a bite before the next).
- `GotoPlayer`: `maxJump`, `attemptTime`, `giveUpTime`, `driver` (the emotion, agitation for all),
  `extraDrive`, `minDrive`, search radii and facing like food seeking.

## Rules (from the code)

- Turning feral switches on going for and biting the player, puts the feral aura on, changes the
  face, and starts the clock: it poofs after `feralLifetimeHours`. When the time is up on The Ranch
  or in the Wilds it gets one more game hour instead (again and again).
- Not feral any more: the player-chasing parts switch off, the aura goes; when cleared with calming,
  agitation drops by 0.5.
- A feral slime eats anything in its diet that touches it, whatever its hunger (it always wants to
  eat). Eating anything except the player clears feral for slimes with `dynamicFromFeral`.
- Going for the player: drive from the emotion (floored, plus extra); it matters drive² × 0.95 (the
  same as food); the player only counts within the search radius (drive ÷ distance² must beat
  1 ÷ radius²). It moves like going for food; jump strength from the drive and `maxJump`. After the
  attempt time it gives up, gains 0.1 agitation and ignores the player for the give-up time.
- Biting: touching the player (or being touched by the player walking into it) while switched on and
  able to chomp, it turns to the player and bites; the bite takes `damagePerAttack` health. Held in
  the vacpack, it bites too. The next chomp can start `timePerAttack` seconds after one ends.
- Stomp: grounded, feral, the player 5–20 m away and 5 s since its last stomp, it matters a random
  0.3–1. It turns toward a point 2 m in front of the player and leaps with a velocity change of
  √(distance × gravity) × 1.2 × 1.4 along (direction + up) normalised. Once it stops rising it drops
  straight down at its current speed; when it lands (a touch at least 0.5 m below its centre, or
  water) it explodes with its stomp numbers (see slime-abilities.md, "Explosions"). It can't rethink
  mid-stomp. Game modes with hostiles switched off never stomp.

## Not done yet (openranch)

- The aura, face, sounds and poof effect; glaring at the player (`GlareAtPlayer`); fear.
- Bites while held in the vacpack; casual mode's "no hostiles".
- Where ferals come from in the world (feral spawners) belongs to the zone work.
