# Jailbreak

A CS2 Jailbreak gamemode plugin built on [SwiftlyS2](https://github.com/swiftlys2/swiftlys2).

---

## Features

### Player Roles & Teams

- **Teams**: Guard (CT), Prisoner (T)
- **Roles**: Warden, Deputy, Rebel, Freeday
- Per-role particle icon above the player's head (configurable via `icons.toml`)
- Per-role model assignment with fallback color tinting:
  - Warden → blue, Deputy → white, Rebel → red, Freeday → green
- Guard and prisoner model pools with random selection each spawn (`models.toml`)
- Cuffed state tracking per player

### Warden System

- Any guard can claim warden with `!w`; only one warden at a time
- Warden auto-assigned from eligible guards after a configurable delay each round
- Warden is removed on death (killed by a prisoner) or round end
- Deputy tracking (one at a time)
- HUD center message updated every 3 seconds showing current warden and deputy

### Cell Door System

- Opens / closes `func_door`, `func_door_rotating`, `func_movelinear`, `prop_door_rotating`, and `func_breakable` entities
- Cells auto-open after a configurable number of seconds each round (`utils.toml → OpenCellsAfterSeconds`)
- State tracking prevents duplicate open/close calls

### Box Mode

- Enables `mp_teammates_are_enemies` so prisoners can damage each other
- Guards (CT) are protected — damage between CTs is blocked while box is active
- Plays a configurable sound on activation (`BoxStartSound`, `BoxStartSoundVolume`)
- Optionally hides overhead teammate names during box (`HideTeammatesName`)
- Box stops automatically on round end

---

## Commands

All commands are configurable in `warden.toml`.

| Command | Default aliases | Description |
|---------|----------------|-------------|
| BecomeWarden | `!w`, `!warden` | Claim the warden role (guards only) |
| GiveUpWarden | `!uw`, `!unwarden` | Voluntarily step down as warden |
| WardenHelp | `!whelp`, `!wh` | Print warden command list |
| WardenMenu | `!wmenu`, `!wm` | Open the interactive warden menu |
| ToggleBox | `!box` | Enable / disable box mode |
| ToggleCells | `!cells`, `!c` | Open / close cell doors |

---

## Warden Menu

Opened with `!wmenu`. Provides two submenus:

- **Toggle Cells** — Open Cells / Close Cells
- **Toggle Box** — Start Box / Stop Box

---

## Configuration

| File | Section | Key settings |
|------|---------|--------------|
| `warden.toml` | Warden | Command aliases, auto-assign delay |
| `icons.toml` | Icons | Particle icon paths per role |
| `models.toml` | Models | Model paths for each role and team pools |
| `utils.toml` | Utils | `OpenCellsAfterSeconds`, `HideTeammatesName`, `BoxStartSound`, `BoxStartSoundVolume` |

---

## Public API

Other plugins can depend on `Jailbreak.Contract` and resolve `IJailbreak` to access:

- `IJBPlayerManagement Players` — full player tracking (get warden, get by role/team, etc.)

# TODO List
- [ ] Make LastRequest interface system + configure it in core.
- [ ] Make SpecialDays interface systen + configure it in core.
- [ ] Create some LastRequest modules.
- [ ] Create some SpecialDays modules.
- [ ] Finish warden commands.
- [ ] Finish deputy commands.
- [ ] Finish configuring deputy and warden roles.
- [ ] Finish configuring rebel system.
- [ ] Finish configuring freeday system.
- [ ] Finish warden menu.
- [ ] Configure team ratio.
- [X] Mute prisoners system (When Warden is speaking, first x seconds of round start, etc...)
- [ ] Add more TODO list 😂
