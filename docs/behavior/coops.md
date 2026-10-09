# Coops: hens laying, chicks growing up

Code: `src/OpenRanch.Ranch/Coops.cs` (the rules), `ProduceData.cs` (the values),
`game/scripts/RanchEconomy/RanchProduce.cs` (in play).

## Hens

A hen's `Reproduce` script names her mate (`nearMateId`, the rooster), what she lays (`childPrefab`),
how often (`minReproduceGameHours` to `maxReproduceGameHours`, 8 to 16 h in the install) and the
animals that crowd her (`densityIds` within `densityDist`, at most `maxDensity`: 12 within 10 m).

When her time comes she lays one chick if a rooster is within `maxDistToMate` (10 m) and no more than
her tolerated crowd is around her (counting herself). Inside a coop she always lays; outside, half
the time. A deluxe coop tolerates `deluxeDensityFactor` times the crowd (2x, rounded half to even).
In a vitamizer's reach she lays a second chick half the time. Whether she laid or not, her next
time is a fresh random period from now. A hen made in play (or one loaded without a time) starts
with a random period from now (never from before a new game's start).

The save keeps each hen's next laying time (`ReproduceTime`).

## Chicks

A chick's `TransformAfterTime` grows it up after `delayGameHours` (6 h) into one of its `options`,
picked by weight (hen 9, rooster 1 in the install). The original keeps a weight map: each option
with a positive weight replaces the pick so far with probability weight over the running total.
Near a feeder the time runs twice as fast. The save keeps each chick's growing-up time
(`TransformTime`).

Checked: static analysis of `Reproduce`, `TransformAfterTime` and `Randoms.Pick`; values read from
the install (`Install_produce_crops_and_hens`). The headless `--m6-check` puts a hen and a rooster in
a new coop and checks the laying time and the chick's growing up.

## Not modelled yet

- A feeder near a chick speeding it up (the coop's `FeederRegion`).
- The egg a new chick hatches from (`EggActivator`).
- Elder hens and roosters (`TransformChanceOnReproduce`).
