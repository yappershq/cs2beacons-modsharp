<div align="center">
  <h1><strong>CS2Beacons</strong></h1>
  <p>Admin command to put a pulsing, beeping, team-colored beacon on any player.</p>
</div>

<p align="center">
  <img src="https://img.shields.io/github/stars/yappershq/cs2beacons-modsharp?style=flat&logo=github" alt="Stars">
</p>

---

A [ModSharp](https://github.com/Kxnrl/modsharp-public) port of the CounterStrikeSharp plugin [`yappershq/cs2beacons`](https://github.com/yappershq/cs2beacons). An admin runs `beacon <target>` to toggle a team-colored particle beacon — parented to the player's pawn and beeping on a repeating timer — so everyone can find them. Useful for highlighting a player (warden/rebel in Jailbreak, a marked target, etc.). Cleaned up automatically on death, disconnect, and round restart.

## 🚀 Install

Copy the build output into your ModSharp install (`<sharp>` = your `sharp` directory):

| From | To |
|------|----|
| `.build/modules/CS2Beacons/` | `<sharp>/modules/CS2Beacons/` |
| `.build/locales/cs2beacons.json` | `<sharp>/locales/cs2beacons.json` |

Restart the server (or change map) to load. The config at `<sharp>/configs/cs2beacons.json` is written with defaults on first run.

Requires the **AdminManager** and **LocalizerManager** modules (ship with ModSharp). Without AdminManager the command stays gated but no permission resolves; without LocalizerManager messages fall back to plain text.

## ⌨️ Commands

| Command | Arguments | Description | Permission |
|---------|-----------|-------------|------------|
| `beacon` | `<name>` or `@all` / `@ct` / `@t` | Toggle a beacon on the matched alive player(s). Partial name match must be unambiguous; `@` selectors may hit many. | `@cs2beacons/beacon` |

The permission is registered with the AdminManager on load (via `MountAdminManifest`), so a root (`*`) admin resolves it automatically — no admin-file edit needed.

## ⚙️ Configuration

`<sharp>/configs/cs2beacons.json` (auto-generated on first run):

| Setting | Default | Meaning |
|---------|---------|---------|
| `BeaconParticleCt` | `particles/player_beacon_ct.vpcf` | Particle effect for CT targets |
| `BeaconParticleT` | `particles/player_beacon_t.vpcf` | Particle effect for T targets |
| `BeaconSoundevtsPath` | `soundevents/soundevents_jailbreak.vsndevts` | Soundevents file precached for the beep |
| `BeaconSoundeventName` | `beacon.blip1` | Soundevent emitted on each beep |
| `BeaconCommandPermission` | `@cs2beacons/beacon` | Admin permission required to run `beacon` |
| `BeaconBeepInterval` | `1.09375` | Seconds between beeps (~70 ticks @ 64-tick) |
| `BeaconSpawnHeight` | `10` | Z offset of the particle above the pawn origin |
| `BeaconToggleVisibility` | `everyone` | Who sees the toggle message: `everyone`, `admins`, or `caller` |

User-facing strings (usage / errors / toggle broadcast) live in `locales/cs2beacons.json` and are served per-client culture via the LocalizerManager.

## 🔧 How it works

Toggling on spawns an `info_particle_system` with the team-colored `vpcf`, teleports it to the pawn origin plus `BeaconSpawnHeight`, parents it to the pawn so it follows the player, and starts it. A single repeating timer beeps every active beacon on `BeaconBeepInterval`, and only runs while at least one beacon is active. Beacons are tracked per slot and removed on death, disconnect, round restart, and game shutdown.

## ⚠️ Custom content required

The beacon needs assets the **base game does not ship**:

- `particles/player_beacon_ct.vpcf`
- `particles/player_beacon_t.vpcf`
- The `beacon.blip1` soundevent inside `soundevents/soundevents_jailbreak.vsndevts`

These must be present in a **mounted workshop/content addon** on the server. If the particles are missing, the command still runs but **nothing renders** (the particle silently no-ops when its `.vpcf` is not precached/mounted). Point the config paths at whatever your content addon provides.

## 🧩 Public API

Other plugins can drive beacons via `ICS2BeaconsApi` (resolve in `OnAllModulesLoaded`):

```csharp
var beacons = sharpModuleManager
    .GetOptionalSharpModuleInterface<ICS2BeaconsApi>(ICS2BeaconsApi.Identity)?.Instance;

beacons?.ToggleBeacon(targetClient, callerClient);
bool active = beacons?.HasActiveBeacon(targetClient) ?? false;
```

## 📦 Build

```bash
dotnet build CS2Beacons.slnx -c Release
```

Outputs `.build/modules/CS2Beacons/CS2Beacons.dll` and the locale at `.build/locales/cs2beacons.json`.

## 🙏 Credits

Port of [yappershq/cs2beacons](https://github.com/yappershq/cs2beacons) (CounterStrikeSharp) by yappersHQ.

---

<div align="center">
  <p>Made with ❤️ by <a href="https://github.com/yappershq">yappersHQ</a></p>
  <p>⭐ Star this repo if you find it useful!</p>
</div>
