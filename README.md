# Jailbreak

A CS2 Jailbreak gamemode plugin built on [SwiftlyS2](https://github.com/swiftlys2/swiftlys2).

## Features

### Player Roles And Teams

- Guard and prisoner team tracking through `JBPlayerManagement`.
- Warden, Deputy, Rebel, Freeday, and cuffed player state.
- Hot reload team sync so role/team state is rebuilt when the plugin reloads.
- CT team ratio enforcement on CT joins only: 1 guard per 2 prisoners, with bots included in the count.
- Per-role particle icon support.
- Per-role and team model assignment through `models.toml`, with fallback tint colors.

### Warden System

- Guards can claim warden with `!w`; only one warden can exist at a time.
- Warden can give up with `!uw`.
- Warden auto-assignment from eligible guards after the configured delay.
- Warden cleanup on death, round end, and plugin unload.
- Cuffs are given to the warden and cleaned up when warden is removed.
- Warden can remove weapons by shooting them.
- HUD center message shows the current warden and deputy.

### Deputy System

- One deputy can exist at a time.
- Deputy can open and close cells.
- Warden menu supports assigning and removing deputy.

### Cells And Box

- Cell manager opens and closes common cell door entities.
- Cells can auto-open after `OpenCellsAfterSeconds` from `utils.toml`.
- Box mode toggles prisoner friendly fire with CT protection.
- Box mode can play a configurable start sound and hide teammate names.
- Box, cells, and menu labels update live when state changes.

### Voice And Freeday

- Warden can mute or unmute all prisoners.
- Warden can mute or unmute individual prisoners.
- Prisoner voice state is shown directly in the menu and updates after changes.
- Warden can give and remove freeday from the menu.
- Freeday state is shown beside each prisoner and updates instantly.

### Visuals

- Warden ping creates a CBeam circle at the ping location.
- Ping beacon grows into place, then stays until the next ping or timeout.
- Player beacon support exists for effects such as duels.
- Warden laser appears while holding `E`, starts from the weapon position, traces against map geometry, and stops at walls/structures.
- Laser has a smooth grow/lerp animation.
- Laser and ping colors support normal colors and rainbow mode.
- Visual color preferences are saved per warden in the database and cached for performance.

## Commands

All warden command aliases are configurable in `warden.toml`.

| Command | Default aliases | Description |
| --- | --- | --- |
| BecomeWarden | `!w`, `!warden` | Claim the warden role. |
| GiveUpWarden | `!uw`, `!unwarden` | Give up the warden role. |
| WardenHelp | `!whelp`, `!wh` | Print warden help. |
| WardenMenu | `!wmenu`, `!wm` | Open the warden menu. |
| ToggleBox | `!box` | Toggle box mode. |
| ToggleCells | `!cells`, `!c` | Open or close cells. |

## Warden Menu

Opened with `!wmenu`.

- Toggle Cells
- Toggle Box
- Toggle Voice
- Manage Deputy
- Manage Freeday
- Visual Management
  - Laser Color
  - Ping Color

## Configuration

| File | Section | Key settings |
| --- | --- | --- |
| `warden.toml` | Warden | Warden command aliases and auto-assign delay. |
| `deputy.toml` | Deputy | Deputy command aliases. |
| `models.toml` | Models | Warden, deputy, rebel, freeday, guard, and prisoner models. |
| `utils.toml` | Utils | Database connection, cell timing, box sound/name settings, and other shared settings. |
| `voice.toml` | Voice | Prisoner mute behavior. |
| `jailbreak.cfg` | Game cvars | Generated in the plugin directory and applied on map start or hot reload. |

## Database

`WardenDatabase` uses `Utils.DatabaseConnection` and creates `jb_warden_settings`.

Stored and cached per warden:

- Laser color
- Laser rainbow mode
- Ping/beam color
- Ping/beam rainbow mode

## Public API

Other plugins can depend on `Jailbreak.Contract` and resolve `IJailbreak`.

- `IJBPlayerManagement Players` gives access to player tracking, warden/deputy lookup, role state, and team state.

## TODO List

- [ ] Build the LastRequest interface system and wire it into core.
- [ ] Build the SpecialDays interface system and wire it into core.
- [ ] Create LastRequest modules.
- [ ] Create SpecialDays modules.
- [x] Finish deputy commands.
- [x] Configure deputy and warden roles.
- [x] Configure rebel system.
- [x] Configure freeday system.
- [x] Finish the current warden menu structure.
- [x] Add live state labels to warden menu options.
- [x] Add visual management for laser and ping colors.
- [x] Persist warden visual settings in the database.
- [x] Add warden ping beacon.
- [x] Add player beacon support.
- [x] Add animated warden laser.
- [x] Configure team ratio.
- [x] Add prisoner mute system.
- [x] Add cuffs to warden.
- [x] Add ability to remove weapons when shooting them as warden.
- [x] Add a `jailbreak.cfg` file in the plugin directory.
- [ ] Revisit drawing mode later.
- [ ] Add more TODO items.
