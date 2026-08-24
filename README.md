# CS2-IdleStopper

A small CounterStrikeSharp plugin that deals with people who go AFK. Nothing fancy, it just watches for key presses and acts when there aren't any.

## What it does

- Counts seconds a living T/CT player goes without pressing anything or moving their aim. Input is checked every frame, so a quick tap or a small mouse nudge is enough to clear it.
- At `notify_seconds` (default 45) it plays a popup sound, shakes their screen, and puts a countdown in the middle of their screen.
- At `action_seconds` (default 75) it either moves them to spectator, kicks them, or does nothing, depending on `action_type`.
- Warmup is skipped completely. Spectators, dead players, and bots are never counted.

The countdown is the gap between the two values, so with the defaults you get a 30 second warning. After they're moved, they get kicked if they sit in spectator for 3 rounds.

## Config

The zip ships a commented `CS2-IdleStopper.example.json`. CounterStrikeSharp copies it to `CS2-IdleStopper.json` the first time the plugin loads, comments and all, so you can read what each setting does right there in the file. Delete the json and restart to get a fresh copy.

```json
{
  "notify_seconds": 45,
  "action_seconds": 75,
  "action_type": 1,
  "round_start_only": false,
  "spectator_kick_rounds": 3,
  "sound_enabled": true,
  "sound": "ui/panorama/popup_reveal_01",
  "sound_interval_seconds": 5,
  "shake_enabled": true,
  "center_message": true,
  "afk_command_enabled": true,
  "afk_command_seconds": 180,
  "admin_immune": true,
  "admin_roles": ["@css/root", "@css/generic"],
  "notify_admins": true,
  "announce_moves": true
}
```

- `action_type`: `0` warning only (timer just restarts), `1` move to spectator, `2` kick.
- `round_start_only`: only count from the start of a round. As soon as a player presses anything that round they are ignored until the next one, so holding an angle for a while does not trigger it.
- `spectator_kick_rounds`: if the plugin moved someone to spectator and they just sit there, kick them after this many round starts. `0` turns it off. Picking a team again stops the count. Defaults to 3.
- `sound`: any client-side sound path, played when the warning starts. Empty string or `sound_enabled: false` turns it off.
- `sound_interval_seconds`: repeat the sound and shake every this many seconds while warned. `0` does it once.
- `shake_enabled`: one second screen shake on the same beat as the sound. No damage and the player does not move.
- `center_message`: `true` shows the live countdown in the middle of the screen. `false` sends a single purple chat message at notify time instead, telling them what happens and how many seconds they have. It does not repeat. The file has short help lines next to each key too. Bad values get clamped, and `action_seconds` is always forced above `notify_seconds`.

- `afk_command_enabled` / `afk_command_seconds`: players can type `!afk` to pause checks on themselves. When the time is up their idle counter starts over from zero.
- `admin_immune` / `admin_roles`: anyone with one of these CounterStrikeSharp flags is never counted.
- `notify_admins`: those same admins get a chat line when someone hits the warning, gets moved or kicked, or uses `!afk`. Only the start of a warning, not every second.
- `announce_moves`: tell the whole server when someone is moved to spectator.

## Commands

`!afk` (or `css_afk` in console) pauses idle checks on yourself for the configured time. The warning message mentions it when the command is enabled.

## Install

Grab the zip from Releases and drop the `addons` folder into your `csgo` folder. It puts the dll and the example config where they belong. Needs CounterStrikeSharp build 364 or newer. Hot reloading is fine.

## Building

```
dotnet build -c Release
```

## License

MIT
