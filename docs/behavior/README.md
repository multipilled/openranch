# Behavior notes

This folder describes how Slime Rancher behaves, one file per system (slimes, vacpack, plots,
economy, zones and so on), written in our own words. openranch's code is written from these notes.

Rules for these notes:

- Describe behavior, rules and numbers as facts ("a slime that has eaten becomes hungry again after
  N game hours"). Never paste code from a decompiler, not even a line, and never paraphrase code
  line by line.
- Gameplay numbers that come from the game's data files (prices, diets, spawn rates) are read from
  the player's copy by the importer. Name the data field here instead of copying its values.
- Say how each fact was checked: watched in game, measured from a recording, or read from the
  game's data.
