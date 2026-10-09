# Contributing to openranch

Thanks for your interest. openranch is a clean-room rewrite, so the rules below protect the project.

## The rules

- **Never commit anything from the game**: no models, textures, sounds, text, shaders, saves, or decompiled code. The
  importer reads your own install at run time; extracted data stays in the git-ignored `gamedata/`. The commit guard
  enforces this: enable it with `git config core.hooksPath .githooks`.
- **Clean room**: describe behaviour in your own words (docs, tests, measurements) and implement it from that
  description. Don't paste or translate decompiled code or the original's compiled shaders.
- Values you could not verify go in `UNVERIFIED.md` with how to check them.

## Getting started

1. Read the README's *Building* section and build with the .NET 8 SDK and Godot 4.7 (.NET edition).
2. Point the importer at your Slime Rancher install and run the checks (`--m2-check`, `--world-check`, ...) and the
   tests (`dotnet test`).
3. Pick an issue labelled `good first issue`, or a milestone row in the README's roadmap, and say in the issue that you
   are on it.

## Pull requests

- One feature or fix per PR, with a test or a headless check (`--*-check`) when the behaviour is testable.
- Say how you verified it against the original game (a reference screenshot, a measurement, a save).
- Keep the style of the surrounding code.

AI-assisted contributions are welcome as long as you have read, built and tested what you submit, and they follow
the clean-room rules above.
