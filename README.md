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
- Warden chat and scoreboard tags are applied while the role is active.
- Gameplay sounds can be configured for warden set/remove, rebel set, cuffs, and Last Request events.

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

### Prisoners And Guards

- Rebels can request surrender with `!s` or `!surrender`.
- The surrender request opens an accept/refuse menu for the current warden.
- Accepted surrenders remove rebel state and strip all prisoner weapons, including knife.
- Guards can use `!guns` to select a primary and secondary weapon.
- Guard gun preferences are saved in the database and restored on spawn outside Special Days.
- Prisoners are stripped on round start outside Special Days and only receive a knife.
- Prisoners can use `!queue` to join the guard queue when guards are full; premium permissions are placed before normal queue entries.

### Special Days

- `Jailbreak.Contract` exposes `ISpecialDay`, `SpecialDayBase`, and shared weapon helpers for external modules.
- Special days can be registered from other plugins through `IJailbreak`.
- Warden can queue a Special Day with `!sd` or from the Warden Menu.
- Queued days start next round and respect the configurable round cooldown.
- Countdown support can freeze all players, prisoners, guards, or nobody depending on the day.
- Countdown HTML shows the day name, remaining time, and day description.
- Special Day weapon restrictions are enforced through acquire checks.
- Optional `!sguns` menu supports selecting a primary first, then a secondary, then gives both weapons.
- Special days can enable friendly fire with `AllowFriendlyFire`.
- Normal Jailbreak systems are blocked during active Special Days, including warden actions, rebels, freedays, cuffs, laser, ping, and warden voice handling.
- Active Special Days keep showing their name and description in center HTML.
- Bunnyhop is enabled instantly during Special Day countdowns and active Special Days when enabled in config.
- End announcements show the Special Day name and surviving players, with names colored by team.

### Current Special Days

| Day | Module | Settings |
| --- | --- | --- |
| Knife Fight     | `Modules/SpecialDays` | Knives only, no guns menu, no countdown freeze, friendly fire enabled. |
| Free For All    | `Modules/SpecialDays` | Everyone is on their own. Players use `!sguns` to select their desired guns. |
| Teleport        | `Modules/SpecialDays` | Players swap positions with the target they shoot. |
| Hide And Seek   | `Modules/SpecialDays` | Guards are frozen during hide time, then prisoners are frozen while guards hunt. |
| War             | `Modules/SpecialDays` | Prisoners are frozen during preparation, guards can prepare with `!sguns`. |
| No Scope        | `Modules/SpecialDays` | Sniper-only FFA with secondary attack blocked to prevent scoping. |
| Scout           | `Modules/SpecialDays` | Scout-only FFA with configurable low gravity. |
| Taser           | `Modules/SpecialDays` | Taser-only FFA. |
| OneInTheChamber | `Modules/SpecialDays` | Classic OITC mode, weapon configurable from config. |
| OnlyHeadshot    | `Modules/SpecialDays` | Onlyheadshot day, all weapons allowed. |
### Current Last Requests

| LR | Module | Settings |
| --- | --- | --- |
| Knife Fight   | `Modules/LastRequests` | Knives only, different knife types. |
| Shot For Shot | `Modules/LastRequests` | Classic shot for shot mode. |

### Visuals

- Warden ping creates a CBeam circle at the ping location.
- Ping beacon grows into place, then stays until the next ping or timeout.
- Player beacon support exists for effects such as duels.
- Warden laser appears while holding `E`, starts from the weapon position, traces against map geometry, and stops at walls/structures.
- Laser has a smooth grow/lerp animation.
- Laser and ping colors support normal colors and rainbow mode.
- Visual color preferences are saved per warden in the database and cached for performance.
- Warden drawing mode can be toggled with `!draw` and draws smoothed persistent CBeam strokes while holding Mouse2.
- Warden can grant temporary prisoner drawing access from the menu; access is cleared on round start.
- Warden can clean up all drawings, their own drawings, or a specific prisoner's drawings from the menu.

## Commands

Command aliases are configurable in their matching config files.

| Command | Default aliases | Description |
| --- | --- | --- |
| BecomeWarden | `!w`, `!warden` | Claim the warden role. |
| GiveUpWarden | `!uw`, `!unwarden` | Give up the warden role. |
| WardenHelp | `!whelp`, `!wh` | Print warden help. |
| WardenMenu | `!wmenu`, `!wm` | Open the warden menu. |
| SpecialDays | `!sd` | Open the Special Days selection menu. |
| ToggleBox | `!box` | Toggle box mode. |
| ToggleCells | `!cells`, `!c` | Open or close cells. |
| ToggleDraw | `!draw` | Toggle warden drawing mode. |
| DrawColor | `!drawcolor` | Open the drawing color menu. |
| DrawClear | `!drawclear`, `!cleardraw` | Clear your own drawings. |
| GuardGuns | `!guns` | Open the guard guns menu. |
| GuardQueue | `!q`, `!queue` | Join the guard queue, or move directly if a guard slot is free. |
| GuardUnqueue | `!uq`, `!unqueue` | Leave the guard queue. |
| GuardQueueList | `!queuelist` | Show the guard queue in chat, HTML, or both depending on config. |
| SpecialGuns | `!sguns` | Open the active Special Day guns menu when enabled. |
| Surrender | `!s`, `!surrender` | Request rebel surrender from the warden. |
| JailbreakStats | `!jbstats`, `!jstats`, `!stats` | Open Last Request and Special Day stats. |

## Warden Menu

Opened with `!wmenu`.

- Toggle Cells
- Toggle Box
- Toggle Voice
- Manage Deputy
- Manage Freeday
- Special Days
- Visual Management
  - Laser Color
  - Ping Color
  - Draw Color
  - Prisoner Draw Access
  - Drawing Cleanup

## Required Workshop Addon

The plugin can run with custom paths, but the built-in model and sound defaults are made for the Jailbreak addon below. Subscribe/add it to your server workshop collection if you want the default `models.toml` and `sounds.toml` values to work out of the box.

<a href="https://steamcommunity.com/sharedfiles/filedetails/?id=3737014592" target="_blank">
  <img
    src="https://img.shields.io/badge/Steam%20Workshop-Jailbreak%20Addon-1b2838?style=for-the-badge&logo=steam&logoColor=white"
    alt="Steam Workshop Jailbreak Addon"
  />
</a>

## Configuration

| File | Section | Key settings |
| --- | --- | --- |
| `warden.toml` | Warden | Warden command aliases and auto-assign delay. |
| `deputy.toml` | Deputy | Deputy command aliases. |
| `specialday.toml` | SpecialDay | Special Day round cooldown. |
| `models.toml` | Models | Warden, deputy, rebel, freeday, guard, and prisoner models. Built-in defaults use the Jailbreak Workshop addon. |
| `utils.toml` | Utils | Database connection, cell timing, box sound/name settings, and other shared settings. |
| `voice.toml` | Voice | Prisoner mute behavior. |
| `sounds.toml` | Sounds | Gameplay sounds, sound event files, and muted sound reasons. Built-in defaults use the Jailbreak Workshop addon. |
| `queue.toml` | GuardQueue | Queue command aliases, list output targets, and premium permission flags. |
| `jailbreak.cfg` | Game cvars | Generated in the plugin directory and applied on map start or hot reload. |

## Database

`WardenDatabase` uses `Utils.DatabaseConnection` and creates `jb_warden_settings`.

Stored and cached per warden:

- Laser color
- Laser rainbow mode
- Ping/beam color
- Ping/beam rainbow mode
- Draw color
- Draw rainbow mode

`JBStatsDB` uses `Utils.DatabaseConnection` and creates `jb_player_stats`.

Stored and cached per player:

- Last Request wins and losses
- Special Day wins and losses

`GuardGunsDatabase` uses `Utils.DatabaseConnection` and creates `jb_guard_guns`.

Stored and cached per guard:

- Preferred primary weapon
- Preferred secondary weapon

## Public API

Other plugins can depend on `Jailbreak.Contract` and resolve `IJailbreak`.

- `IJBPlayerManagement Players` gives access to player tracking, warden/deputy lookup, role state, and team state.
- `RegisterSpecialDay(ISpecialDay specialDay)` registers a Special Day module.
- `UnregisterSpecialDay(string id)` unregisters a Special Day module.

## Releases

Releases are created from tags through `.github/workflows/release.yml`.

- Beta prereleases use tags like `v0.1.0-beta.1`.
- Stable releases use tags like `v0.1.0`.
- Release notes are generated from commits since the previous matching release tag and include clickable commit hashes.

## TODO List

- [X] Build the LastRequest interface system and wire it into core.
- [x] Build the SpecialDays interface system and wire it into core.
- [X] Create LastRequest modules.
- [x] Create first SpecialDays module.
- [x] Add more SpecialDays modules.
- [ ] Add more LastRequests modules.
- [x] Save Last Request wins and losses in database.
- [x] Save Special Day wins and losses in database.
- [x] Add Jailbreak stats command and menu.
- [x] Show LR winner win count in end announcements.
- [x] Show Special Day winner/winners with win counts in end announcements.
- [x] Constantly keep SpecialDay description in CenterHTML when active.
- [x] Add rebel surrender requests.
- [x] Add guard guns menu and saved guard loadouts.
- [x] Strip prisoner weapons on round start.
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
- [x] Fix bot identity handling for cuffs and player cache.
- [x] Add ability to remove weapons when shooting them as warden.
- [x] Block becoming warden during round end.
- [x] Add a `jailbreak.cfg` file in the plugin directory.
- [x] Add warden drawing mode.
- [x] Add drawing color selection.
- [x] Add drawing cleanup command.
- [x] Add broader drawing cleanup/management options.
- [x] Add gameplay sounds for warden, rebels, cuffs, and Last Requests.
- [x] Add warden tag (both chat and scoreboard).
- [x] Add warden ability to give prisoners draw access.
- [x] Add queue system for the guardians team if full. (!q, !queue, !uq, !unqueue)
- [x] Add premium flag to queue system (automaticly in front of the list unlike normal players)
- [x] Add queue list command with chat and HTML output.

- [X] Add Teleport        day.
- [X] Add HideAndSeek     day.
- [X] Add War             day.
- [X] Add NoScope         day.
- [X] Add Scout           day.
- [X] Add Taser           day.
- [X] Add OneInTheChamber day.
- [X] Add OnlyHeadshot    day.

- [X] Add ShotForShot LastRequest
- [ ] Add MagForMag   LastRequest

- [ ] Add more TODO items.

## ☕ If you'd like to support me, any donation is deeply appreciated!

<a href="https://buymeacoffee.com/t3marius" target="_blank">
  <img
    src="https://cdn.buymeacoffee.com/buttons/v2/default-yellow.png"
    alt="Buy Me A Coffee"
    height="60"
  />
</a>
