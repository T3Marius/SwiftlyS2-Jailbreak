using Jailbreak.Contract;
using SwiftlyS2.Core.Menus.OptionsBase;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Menus;
using SwiftlyS2.Shared.Players;

namespace Jailbreak;

public sealed class WardenMenu
{
    private readonly ISwiftlyCore _core;
    private readonly CellManager _cellManager;
    private readonly BoxManager _boxManager;
    private readonly IJBPlayerManagement _players;

    public WardenMenu(ISwiftlyCore core, CellManager cellManager, BoxManager boxManager, IJBPlayerManagement players)
    {
        _core = core;
        _cellManager = cellManager;
        _boxManager = boxManager;
        _players = players;
    }

    // ─── Root ────────────────────────────────────────────────────────────────

    public void Show(IJBPlayer player)
    {
        var builder = _core.MenusAPI.CreateBuilder().Design
            .SetMenuTitle(player.Localizer["warden_menu.title"]);

        builder.AddOption(new SubmenuMenuOption(player.Localizer["warden_menu_option.toggle_cells"], () => CellsSubmenu(player)));
        builder.AddOption(new SubmenuMenuOption(player.Localizer["warden_menu_option.toggle_box"], () => BoxSubmenu(player)));
        builder.AddOption(new SubmenuMenuOption(player.Localizer["warden_menu_option.toggle_voice"], () => VoiceSubmenu(player)));
        builder.AddOption(new SubmenuMenuOption(player.Localizer["warden_menu_option.manage_deputy"], () => DeputySubmenu(player)));

        _core.MenusAPI.OpenMenuForPlayer(player.Player, builder.Build());
    }

    // ─── Cells ───────────────────────────────────────────────────────────────

    private IMenuAPI CellsSubmenu(IJBPlayer player)
    {
        var builder = _core.MenusAPI.CreateBuilder().Design
            .SetMenuTitle(player.Localizer["cells_submenu.title"]);

        var openCells = new ButtonMenuOption(player.Localizer["cells_submenu_option.open_cells"])
        {
            // Only actionable when cells are currently closed
            Enabled = !_cellManager.CellsOpen
        };
        openCells.Click += (_, _) =>
        {
            _core.Scheduler.NextWorldUpdate(() =>
            {
                if (_cellManager.CellsOpen) return;
                _cellManager.OpenCells();
                _players.SendMessage(MessageType.Chat, "cells_opened_warden");
            });
            return ValueTask.CompletedTask;
        };

        var closeCells = new ButtonMenuOption(player.Localizer["cells_submenu_option.close_cells"])
        {
            // Only actionable when cells are currently open
            Enabled = _cellManager.CellsOpen
        };
        closeCells.Click += (_, _) =>
        {
            _core.Scheduler.NextWorldUpdate(() =>
            {
                if (!_cellManager.CellsOpen) return;
                _cellManager.CloseCells();
                _players.SendMessage(MessageType.Chat, "cells_closed_warden");
            });
            return ValueTask.CompletedTask;
        };

        builder.AddOption(openCells);
        builder.AddOption(closeCells);

        return builder.Build();
    }

    // ─── Box ─────────────────────────────────────────────────────────────────

    private IMenuAPI BoxSubmenu(IJBPlayer player)
    {
        var builder = _core.MenusAPI.CreateBuilder().Design
            .SetMenuTitle(player.Localizer["box_submenu.title"]);

        var startBox = new ButtonMenuOption(player.Localizer["box_submenu_option.start_box"])
        {
            Enabled = !_boxManager.BoxEnabled
        };
        startBox.Click += (_, _) =>
        {
            _core.Scheduler.NextWorldUpdate(() =>
            {
                if (_boxManager.BoxEnabled) return;
                _boxManager.StartBox();
                _players.SendMessage(MessageType.Chat, "box_enabled");
            });
            return ValueTask.CompletedTask;
        };

        var stopBox = new ButtonMenuOption(player.Localizer["box_submenu_option.stop_box"])
        {
            Enabled = _boxManager.BoxEnabled
        };
        stopBox.Click += (_, _) =>
        {
            _core.Scheduler.NextWorldUpdate(() =>
            {
                if (!_boxManager.BoxEnabled) return;
                _boxManager.StopBox();
                _players.SendMessage(MessageType.Chat, "box_disabled");
            });
            return ValueTask.CompletedTask;
        };

        builder.AddOption(startBox);
        builder.AddOption(stopBox);

        return builder.Build();
    }

    // ─── Voice ───────────────────────────────────────────────────────────────

    private IMenuAPI VoiceSubmenu(IJBPlayer player)
    {
        var builder = _core.MenusAPI.CreateBuilder().Design
            .SetMenuTitle(player.Localizer["voice_submenu.title"]);

        // Per-prisoner submenus listed first for quick access
        foreach (var prisoner in _players.GetPlayersByTeam(JBTeam.Prisoner))
        {
            // Show current mute state in the label so the warden can see at a glance
            var label = prisoner.IsMuted
                ? player.Localizer["voice_submenu_option.prisoner_muted_label", prisoner.Player.Name]
                : player.Localizer["voice_submenu_option.prisoner_unmuted_label", prisoner.Player.Name];

            builder.AddOption(new SubmenuMenuOption(label, () => PrisonerVoiceSubmenu(player, prisoner)));
        }

        // Bulk actions at the bottom
        var unmuteAll = new ButtonMenuOption(player.Localizer["voice_submenu_option.unmute_all_prisoner"]);
        unmuteAll.Click += (_, _) =>
        {
            _core.Scheduler.NextWorldUpdate(() =>
            {
                foreach (var p in _players.GetPlayersByTeam(JBTeam.Prisoner))
                {
                    p.Unmute();
                    p.WasUnmutedByWarden = true;
                }
                _players.SendMessage(MessageType.Chat, "all_prisoners_unmuted_warden", true);
            });
            return ValueTask.CompletedTask;
        };

        var muteAll = new ButtonMenuOption(player.Localizer["voice_submenu_option.mute_all_prisoner"]);
        muteAll.Click += (_, _) =>
        {
            _core.Scheduler.NextWorldUpdate(() =>
            {
                foreach (var p in _players.GetPlayersByTeam(JBTeam.Prisoner))
                {
                    p.Mute();
                    p.WasUnmutedByWarden = false;
                }
                _players.SendMessage(MessageType.Chat, "all_prisoners_muted_warden", true);
            });
            return ValueTask.CompletedTask;
        };

        builder.AddOption(unmuteAll);
        builder.AddOption(muteAll);

        return builder.Build();
    }

    private IMenuAPI PrisonerVoiceSubmenu(IJBPlayer player, IJBPlayer prisoner)
    {
        var builder = _core.MenusAPI.CreateBuilder().Design
            .SetMenuTitle(prisoner.Player.Name);

        var mute = new ButtonMenuOption(player.Localizer["voice_submenu_option.mute_prisoner"])
        {
            // Disable if already muted
            Enabled = !prisoner.IsMuted
        };
        mute.Click += (_, _) =>
        {
            _core.Scheduler.NextWorldUpdate(() =>
            {
                prisoner.Mute();
                prisoner.WasUnmutedByWarden = false;
                _players.SendMessage(MessageType.Chat, "prisoner_muted_warden", true, args: prisoner.Player.Name);
            });
            return ValueTask.CompletedTask;
        };

        var unmute = new ButtonMenuOption(player.Localizer["voice_submenu_option.unmute_prisoner"])
        {
            // Disable if already unmuted
            Enabled = prisoner.IsMuted
        };
        unmute.Click += (_, _) =>
        {
            _core.Scheduler.NextWorldUpdate(() =>
            {
                prisoner.Unmute();
                prisoner.WasUnmutedByWarden = true;
                _players.SendMessage(MessageType.Chat, "prisoner_unmuted_warden", true, args: prisoner.Player.Name);
            });
            return ValueTask.CompletedTask;
        };

        builder.AddOption(mute);
        builder.AddOption(unmute);

        return builder.Build();
    }

    // ─── Deputy ──────────────────────────────────────────────────────────────

    private IMenuAPI DeputySubmenu(IJBPlayer player)
    {
        var builder = _core.MenusAPI.CreateBuilder().Design
            .SetMenuTitle(player.Localizer["manage_deputy_submenu.title"]);

        var currentDeputy = _players.GetDeputy();

        // Informational label showing who (if anyone) is currently deputy
        var statusLabel = currentDeputy != null
            ? player.Localizer["manage_deputy_submenu_option.current_deputy", currentDeputy.Player.Name]
            : player.Localizer["manage_deputy_submenu_option.current_deputy_none"];

        builder.AddOption(new TextMenuOption(statusLabel));

        // Remove deputy — only actionable when one exists
        var removeDeputy = new ButtonMenuOption(player.Localizer["manage_deputy_submenu_option.remove_deputy"])
        {
            Enabled = currentDeputy != null
        };
        removeDeputy.Click += (_, _) =>
        {
            _core.Scheduler.NextWorldUpdate(() =>
            {
                var deputy = _players.GetDeputy();
                if (deputy == null) return;

                deputy.SetDeputy(false);
                _players.SendMessage(MessageType.Chat, "deputy_removed_warden", true, args: deputy.Player.Name);
            });
            return ValueTask.CompletedTask;
        };
        builder.AddOption(removeDeputy);

        // Assign from alive guards (exclude the warden themselves and the current deputy)
        var candidates = _players.GetPlayersByTeam(JBTeam.Guard)
            .Where(g => !g.IsWarden && !(g.IsDeputy))
            .ToList();

        if (candidates.Count > 0)
        {
            builder.AddOption(new TextMenuOption(player.Localizer["manage_deputy_submenu_option.assign_deputy_header"]));

            foreach (var guard in candidates)
            {
                var guardRef = guard; // capture for closure
                var assignOption = new ButtonMenuOption(guardRef.Player.Name);
                assignOption.Click += (_, _) =>
                {
                    _core.Scheduler.NextWorldUpdate(() =>
                    {
                        // Remove previous deputy first if one exists
                        var previousDeputy = _players.GetDeputy();
                        if (previousDeputy != null)
                        {
                            previousDeputy.SetDeputy(false);
                            _players.SendMessage(MessageType.Chat, "deputy_removed_warden", true, args: previousDeputy.Player.Name);
                        }

                        guardRef.SetDeputy(true);
                        _players.SendMessage(MessageType.Chat, "deputy_assigned_warden", true, args: guardRef.Player.Name);
                    });
                    return ValueTask.CompletedTask;
                };
                builder.AddOption(assignOption);
            }
        }

        return builder.Build();
    }
}