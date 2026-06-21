# CS2Beacons (ModSharp)

A [ModSharp](https://github.com/Kxnrl/modsharp-public) port of the CounterStrikeSharp plugin
[`yappersHQ/cs2beacons`](https://github.com/yappersHQ/cs2beacons).

An admin command toggles a glowing, beeping **beacon** on a target player — a team-colored
particle that follows the player and emits a beep on a repeating interval. Useful for
highlighting a player (e.g. the warden/rebel in Jailbreak, a marked target, etc.).

## Command

```
beacon <name | @target>
```

- `beacon <partial name>` — toggle on the single alive player whose name matches (must be unambiguous).
- `beacon @all` — toggle on every alive player.
- `beacon @ct` / `beacon @t` — toggle on every alive player of that team.

Admin-gated. Requires the permission configured by `BeaconCommandPermission`
(default `@cs2beacons/beacon`). The plugin registers this permission with the
AdminManager on load (via `MountAdminManifest`), so a root (`*`) admin resolves it
automatically — no `admins.jsonc` edit needed.

The beacon is automatically removed on player **death**, **disconnect**, and **round restart**.

## Public API

The plugin publishes `ICS2BeaconsApi` to ModSharp's `ISharpModuleManager` (identity
`ICS2BeaconsApi`) so other plugins can drive beacons:

```csharp
var beacons = sharpModuleManager
    .GetOptionalSharpModuleInterface<ICS2BeaconsApi>(ICS2BeaconsApi.Identity)?.Instance;

beacons?.ToggleBeacon(targetClient, callerClient);
bool active = beacons?.HasActiveBeacon(targetClient) ?? false;
```

## Configuration

Written to `<sharp>/configs/cs2beacons.json` on first run:

| Key | Default | Description |
|-----|---------|-------------|
| `BeaconParticleCt` | `particles/player_beacon_ct.vpcf` | CT-team beacon particle |
| `BeaconParticleT` | `particles/player_beacon_t.vpcf` | T-team beacon particle |
| `BeaconSoundevtsPath` | `soundevents/soundevents_jailbreak.vsndevts` | Soundevents file to precache |
| `BeaconSoundeventName` | `beacon.blip1` | Soundevent fired each beep |
| `BeaconCommandPermission` | `@cs2beacons/beacon` | Admin permission for the command |
| `BeaconBeepInterval` | `1.09375` | Seconds between beeps (≈70 ticks @ 64-tick) |
| `BeaconSpawnHeight` | `10` | Z offset of the particle above the pawn origin |
| `BeaconToggleVisibility` | `everyone` | Who sees the toggle message: `everyone` / `admins` / `caller` |
| `BeaconToggleMessage` | `Beacon toggled on {0}` | Toggle chat message (`{0}` = player name) |

## ⚠️ Custom content required

The beacon needs custom assets that the **base game does not ship**:

- `particles/player_beacon_ct.vpcf`
- `particles/player_beacon_t.vpcf`
- The `beacon.blip1` soundevent inside `soundevents/soundevents_jailbreak.vsndevts`

These must be present in a **mounted workshop/content addon** on the server. If the
particles are missing, the command still runs and the beep can still fire, but **nothing
renders** (the particle silently no-ops when its `.vpcf` is not precached/mounted).
Point the config paths at whatever your content addon actually provides.

## Building

```bash
dotnet build CS2Beacons.slnx -c Release
```

Output: `.build/modules/CS2Beacons/CS2Beacons.dll`. Drop the `CS2Beacons` folder into
`<sharp>/modules/`. The plugin compiles against `Sharp.Modules.AdminManager.Shared`
(bundled in `refs/` for compile-time only — the server provides the runtime copy).

## License

MIT
