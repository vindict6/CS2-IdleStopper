# CS2-IdleStopper

A small CounterStrikeSharp plugin that deals with people who go AFK. Nothing fancy, it just watches for key presses and acts when there aren't any.

## What it does

- Counts seconds a living T/CT player goes without pressing anything.
- At `notify_seconds` (default 30) it plays a popup sound, starts slapping them every two seconds for 0 damage, and puts a countdown in the middle of their screen.
- At `action_seconds` (default 60) it either moves them to spectator, kicks them, or does nothing, depending on `action_type`.
- Warmup is skipped completely. Spectators, dead players, and bots are never counted.

The countdown is the gap between the two values, so with the defaults you get a 30 second warning.

## Config

Generated on first load at `addons/counterstrikesharp/configs/plugins/CS2-IdleStopper/CS2-IdleStopper.json`.

```json
{
  "notify_seconds": 30,
  "action_seconds": 60,
  "action_type": 1
}
```

`action_type`: `0` warning only (timer just restarts), `1` move to spectator, `2` kick. The file has short help lines next to each key too. Bad values get clamped, and `action_seconds` is always forced above `notify_seconds`.

## Install

Grab the zip from Releases and drop the folder into `addons/counterstrikesharp/plugins/`. Needs CounterStrikeSharp build 364 or newer. Hot reloading is fine.

## Building

```
dotnet build -c Release
```

## License

MIT
