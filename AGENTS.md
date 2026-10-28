# AGENTS.md

Notes for anyone, human or AI agent, changing this code. `README.md` covers playing the game, its controls and
downloads; this file covers how the code is put together and the rules that keep it working.

## What this is

A recreation of Cinematronics' 1980 vector arcade game Star Castle, in C# on .NET 9 and Avalonia 11. One
self-contained executable per platform (Windows, macOS and Linux, x64 and ARM). The graphics are glowing lines
drawn in code, and the sound effects and the background drone are synthesised by `tools/make_sounds.py`.

This is an older, simpler project than the later AVA games: a single project at the repo root, no tests, no
`global.json` and no `Directory.Build.props`.

## Commands

```bash
dotnet build AVACastle.sln              # needs the .NET 9 SDK or later (the project targets net9.0)
dotnet run --project AVACastle.csproj   # play it; it takes no arguments of its own
python3 tools/make_sounds.py            # rebuild Sounds/ (standard-library Python only)
```

There are no tests yet.

## Layout

- `Engine/`: the game logic.
  - `GameEngine.cs` runs the states, the game loop, collisions, scoring, high scores and the attract cycle.
  - `AttractPilot.cs` is the computer pilot that flies the attract-mode demo.
  - `InputHandler.cs` (keys and mouse) and `ControllerManager.cs` (SDL2 gamepads) answer questions such as
    `IsThrusting` and `IsShooting`.
  - `HighScoreTable.cs`, `InitialsEntry.cs` and `SoundManager.cs` do what their names say.
- `Models/`: `GameObject` and its kinds (`Ship`, `Bullet`, `Fireball`, `Mine`), plus `Ring`, `Castle` (the three
  rings and the tracking cannon) and `Explosion`.
- `Views/GameCanvas.cs`: scales the fixed 1024x768 playfield to the window, draws it, and runs the game from a
  `DispatcherTimer` (about 60 Hz). Ctrl+S saves a screenshot to the Pictures folder.
- `Sounds/`: the generated WAV effects and the four drone loops, copied next to the executable.
- `Images/`: the README's screenshots.

## Rules that keep it working

- **Variable time step.** `GameEngine.Update(deltaTime)` takes the real time since the last tick, capped at
  0.1 s. Everything that moves must scale by `deltaTime`. Random numbers come from unseeded `Random` instances
  and `Random.Shared`, so play is not repeatable.
- **Input goes through `InputHandler`.** On the stick, negative Y is up (thrust). Add new controls there, so keys,
  mouse and gamepad stay in step.
- **SDL under one lock.** `ControllerManager` starts, polls and stops SDL only inside `lock (Sdl)`. Keep it that
  way: unlocked SDL calls from two threads corrupt its heap.
- **Audio.** `SoundManager` plays the WAV files from `Sounds/` through NetCoreAudio, which calls the system's
  player (`aplay` from alsa-utils on Linux). Without one the game still runs, silently. The attract mode
  silences it with `IsSilenced`, leaving the player's mute setting alone.
- **High scores** live in `highscores.txt` in the app-data folder (`AVACastle`), one `INITIALS|score|castle` line
  per entry, written to a temporary file and swapped in.
- **Line endings.** Keep each file's existing line endings (some files, such as `Program.cs` and
  `Engine/ControllerManager.cs`, are CRLF) so diffs show only real changes.

## Making a change

- Build with no new warnings, and play the change by hand: there are no tests to catch mistakes.
- Update `README.md` (features, controls, project structure) to match.
- New sounds go into `tools/make_sounds.py`, not as hand-edited WAV files.

## CI and releases

`.github/workflows/build.yml` builds every push with the .NET 9 SDK (it runs no tests). It then publishes
self-contained single-file builds for win-x64/x86/arm64, linux-x64/arm64/arm and osx-x64/arm64. Every run keeps
the builds as workflow artifacts. Only pushes to `main` replace the assets of the `latest` pre-release, and a
`v*` tag makes a proper release.
