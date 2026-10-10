# DeathTimer

Per-level time limit. When the time runs out, every player dies.

R.E.P.O. BepInEx mod. Host-side only (the countdown display needs the mod on each player).

## Features

- The timer starts when a level begins (shop and arena are not timed)
- When the time runs out, every living player is killed; players revived afterwards are killed again
- Optionally stops the timer once every extraction point is completed
- Countdown on the HUD, red in the last seconds, `TIME UP` when expired
- Only the host needs the mod for the time limit to apply to everyone

## Multiplayer

| Who has the mod | Time limit / death | Countdown display |
|---|---|---|
| Host only | Applies to everyone | Host only |
| Host and some players | Applies to everyone | Players with the mod |

The host owns the timer. Players are killed through the game's own death RPC, so players
without the mod die too. The timer is shared through Photon room properties.

## Installation

Requires [BepInEx 5.x](https://thunderstore.io/c/repo/p/BepInEx/BepInExPack/).

Place `DeathTimer.dll` in `BepInEx/plugins/`.

Configuration file is generated at `BepInEx/config/maxenterme.DeathTimer.cfg` on first launch.

## Configuration

Timer settings are read from the host. HUD settings are per player.

All settings can also be changed in-game with [REPOConfig](https://thunderstore.io/c/repo/p/nickklmao/REPOConfig/)
and take effect immediately: changing `TimeLimitSeconds` moves the running timer's deadline
(measured from the level start), and turning `Enabled` off clears the current timer
(turning it back on starts a fresh one).

| Section | Key | Default | Range | Description |
|---------|-----|---------|-------|-------------|
| Timer | Enabled | true | - | Kill every player when the time limit runs out |
| Timer | TimeLimitSeconds | 600 | 10-7200 | Time limit per level in seconds |
| Timer | StopWhenAllExtracted | true | - | Stop the timer once every extraction point is completed |
| HUD | Enabled | true | - | Show the remaining time |
| HUD | PositionX | 0 | -1000-1000 | X offset from top center |
| HUD | PositionY | -10 | -1000-1000 | Y offset from top center |
| HUD | FontSize | 32 | 10-80 | Font size |
| HUD | WarningSeconds | 60 | 0-600 | Show the timer in red at or below this many seconds |

## Build

```bash
dotnet build -c Release
```
