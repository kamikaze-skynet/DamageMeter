# DamageMeter

A [BepInEx](https://github.com/BepInEx/BepInEx) IL2CPP mod for **The Spell Brigade**: a live overlay with each player's DPS and total damage, broken down by spell.

See [`thunderstore/README.md`](thunderstore/README.md) for the player-facing description and config.

## Building

```powershell
dotnet build src\DamageMeter.csproj -c Release -p:GameDir="D:\SteamLibrary\steamapps\common\The Spell Brigade"
.\package.ps1 -GameDir "D:\SteamLibrary\steamapps\common\The Spell Brigade"
```

## How it works

- Enemy health is removed on the host in `EnemyHealthContainer.RemoveHealthOnOwner`, which calls `RecordDamageDealtToEnemy(amount, damageInfo)`. That call feeds the game's own damage stats (`OnSpellDamageDoneEventData`).
- A postfix there sorts the hit by the damage info's class: `PlayerSpellDamageInfo` (player and spell), other `PlayerDamageInfo`, `PlayerExplosionDamageInfo` (owner), or `InfusedTornadoDamageInfo` (the infusing player). The class is checked once per IL2CPP class pointer, and fields are read straight from memory, because this runs on every hit.
- The host keeps a total, per-source totals, and 1-second buckets per player for the DPS window. About once a second it sends a compact text snapshot to clients as a Netcode named message (`girthy_mods.DamageMeter.Snapshot`).
- `Overlay` is an injected `MonoBehaviour`: one TextMeshPro text in a ScreenSpaceOverlay canvas, laid out in columns with `<pos>` tags.
