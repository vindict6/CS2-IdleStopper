# CS2-IdleStopper

A small CounterStrikeSharp plugin that deals with people who go AFK. Nothing fancy, it just watches for key presses and acts when there aren't any.

## What it does

- Counts seconds a living T/CT player goes without pressing anything.
- At `notify_seconds` (default 30) it plays a popup sound, shakes their screen every two seconds, and puts a countdown in the middle of their screen.
- At `action_seconds` (default 60) it either moves them to spectator, kicks them, or does nothing, depending on `action_type`.
- Warmup is skipped completely. Spectators, dead players, and bots are never counted.

The countdown is the gap between the two values, so with the defaults you get a 30 second warning.

## Config

Generated on first load at `addons/counterstrikesharp/configs/plugins/CS2-IdleStopper/CS2-IdleStopper.json`.

```json
{
  "notify_seconds": 30,
  "action_seconds": 60,
  "action_type": 1,
  "sound_enabled": true,
  "sound": "ui/panorama/popup_reveal_01",
  "shake_enabled": true,
  "center_message": true
}
```

- `action_type`: `0` warning only (timer just restarts), `1` move to spectator, `2` kick.
- `sound`: any client-side sound path, played once when the warning starts. Empty string or `sound_enabled: false` turns it off.
- `shake_enabled`: screen shake every two seconds during the warning. No damage and the player does not move.
- `center_message`: `true` shows the live countdown in the middle of the screen. `false` sends a single purple chat message at notify time instead, telling them what happens and how many seconds they have. It does not repeat. The file has short help lines next to each key too. Bad values get clamped, and `action_seconds` is always forced above `notify_seconds`.

## Install

Grab the zip from Releases and drop the folder into `addons/counterstrikesharp/plugins/`. Needs CounterStrikeSharp build 364 or newer. Hot reloading is fine.

## Building

```
dotnet build -c Release
```

## License

MIT
