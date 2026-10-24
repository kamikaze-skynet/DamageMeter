# DamageMeter

A live damage meter for The Spell Brigade.

- **Per player.** Shows damage per second over the last 10 seconds, total damage, and each player's share of the party's damage.
- **Per spell.** Lists each player's damage by spell, with each spell's share of that player's total. Infused tornadoes and explosions get their own lines.
- **Same numbers for everyone.** The host counts the damage, in the same place the game counts it for its end-of-run stats. The host sends the numbers to every player with the mod about once a second.
- **Run timer** in the header. The last run's numbers stay up until the next run starts.

The host needs the mod for anyone to see numbers in multiplayer. Clients who don't have it aren't affected.

## Showing and hiding

- Click **X** on the meter to hide it. A small **DAMAGE** tab stays in its place; click the tab to bring the meter back.
- Click **Spells** to turn the per-spell lines on or off.
- The keys below do the same. Your choice is remembered between games.

## Keys

| Key | What it does |
|---|---|
| F3 | Show or hide the meter |
| F4 | Show or hide the per-spell lines |

## Config (`girthy_mods.DamageMeter.cfg`)

| Setting | Default | What it does |
|---|---|---|
| `General.Enabled` | `true` | Count damage and show the meter. |
| `General.DpsWindowSeconds` | `10` | DPS covers the last this-many seconds of game time. Shops and pauses don't count. |
| `Display.Visible` | `true` | Shows the meter. F3 flips this. |
| `Display.ShowSpells` | `true` | Shows the per-spell lines. F4 flips this. |
| `Display.ShowTabWhenHidden` | `true` | Leaves a clickable DAMAGE tab while the meter is hidden. |
| `Display.ShowWhenSolo` | `true` | Shows the meter in single player too. |
| `Display.KeepAfterRun` | `true` | Keeps the last run's numbers until the next run starts. |
| `Display.MaxSpellsPerPlayer` | `8` | Spell lines per player. Any spells beyond this are summed as "other". |
| `Display.Position` | `Left` | Where the meter sits: Left, Right, TopLeft, TopRight, BottomLeft or BottomRight. |
| `Display.OffsetX` / `OffsetY` | `20` / `60` | Distance from the screen edge, in pixels at 1080p. |
| `Display.Scale` | `1` | Size of the meter. |
| `Display.BackgroundOpacity` | `0.7` | `0` means no background. |
| `Keys.ToggleKey` / `SpellsKey` / `ResetKey` | `F3` / `F4` / none | Unity Input System key names. The reset key (host only) clears the numbers mid-run. |
