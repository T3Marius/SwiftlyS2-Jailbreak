using Jailbreak.Contract;
using SwiftlyS2.Core.Menus.OptionsBase;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Menus;
using SwiftlyS2.Shared.Players;

namespace Jailbreak;

public sealed class WardenMenu
{
    private readonly ISwiftlyCore        _core;
    private readonly CellManager         _cellManager;
    private readonly BoxManager          _boxManager;
    private readonly IJBPlayerManagement _players;

    public WardenMenu(ISwiftlyCore core, CellManager cellManager, BoxManager boxManager, IJBPlayerManagement players)
    {
        _core = core;
        _cellManager = cellManager;
        _boxManager = boxManager;
        _players = players;
    }

    public void Show(IJBPlayer player)
    {
        var builder = _core.MenusAPI.CreateBuilder().Design
            .SetMenuTitle(player.Localizer["warden_menu.title"]);

        var toggleCells = new SubmenuMenuOption(player.Localizer["warden_menu_option.toggle_cells"], () => ToggleCellsSubmenu(player));
        var toggleBox = new SubmenuMenuOption(player.Localizer["warden_menu_option.toggle_box"], () => ToggleBoxSubmenu(player));
        var toggleVoice = new SubmenuMenuOption(player.Localizer["warden_menu_option.toggle_voice"], () => ToggleVoiceSubmenu(player));


        builder.AddOption(toggleCells);
        builder.AddOption(toggleBox);
        builder.AddOption(toggleVoice);

        var built = builder.Build();
        _core.MenusAPI.OpenMenuForPlayer(player.Player, built);
        
    }
    private IMenuAPI ToggleVoiceSubmenu(IJBPlayer player)
    {
        var builder = _core.MenusAPI.CreateBuilder().Design
            .SetMenuTitle(player.Localizer["voice_submenu.title"]);

        var unmuteAllPrisoner = new ButtonMenuOption(player.Localizer["voice_submenu_option.unmute_all_prisoner"]);
        unmuteAllPrisoner.Click += (sender, args) =>
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
        var muteAllPrisoner = new ButtonMenuOption(player.Localizer["voice_submenu_option.mute_all_prisoner"]);
        muteAllPrisoner.Click += (sender, args) => 
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

        foreach (var prisoner in _players.GetPlayersByTeam(JBTeam.Prisoner))
        {
            var prisonerSubMenu = new SubmenuMenuOption(prisoner.Player.Name, () => TogglePrisonerVoiceSubmenu(player, prisoner));
            builder.AddOption(prisonerSubMenu);
        }

        builder.AddOption(unmuteAllPrisoner);
        builder.AddOption(muteAllPrisoner);

        return builder.Build();
    }
    private IMenuAPI TogglePrisonerVoiceSubmenu(IJBPlayer player, IJBPlayer prisoner)
    {
        var builder = _core.MenusAPI.CreateBuilder().Design
            .SetMenuTitle(prisoner.Player.Name);

        var mute = new ButtonMenuOption(player.Localizer["voice_submenu_option.mute_prisoner"]);
        mute.Click += (sender, args) =>
        {
            _core.Scheduler.NextWorldUpdate(() =>
            {
                prisoner.Mute();
                prisoner.WasUnmutedByWarden = false;
                _players.SendMessage(MessageType.Chat, "prisoner_muted_warden", true, args: prisoner.Player.Name);
            });
            return ValueTask.CompletedTask;
        };

        var unmute = new ButtonMenuOption(player.Localizer["voice_submenu_option.unmute_prisoner"]);
        unmute.Click += (sender, args) =>
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
    private IMenuAPI ToggleBoxSubmenu(IJBPlayer player)
    {
        var builder = _core.MenusAPI.CreateBuilder().Design
            .SetMenuTitle(player.Localizer["box_submenu.title"]);

        var startBox = new ButtonMenuOption(player.Localizer["box_submenu_option.start_box"]);
        startBox.Click += (sender, args) =>
        {
            _core.Scheduler.NextWorldUpdate(() =>
            {
                if (_boxManager.BoxEnabled)
                    return;

                _boxManager.StartBox();
                _players.SendMessage(MessageType.Chat, "box_enabled");
            });
            return ValueTask.CompletedTask;
        };

        var stopBox = new ButtonMenuOption(player.Localizer["box_submenu_option.stop_box"]);
        stopBox.Click += (sender, args) =>
        {
            _core.Scheduler.NextWorldUpdate(() =>
            {
                if (!_boxManager.BoxEnabled)
                    return;

                _boxManager.StopBox();
                _players.SendMessage(MessageType.Chat, "box_disabled");
            });
            return ValueTask.CompletedTask;  
        };


        builder.AddOption(startBox);
        builder.AddOption(stopBox);

        return builder.Build();
    }
    private IMenuAPI ToggleCellsSubmenu(IJBPlayer player)
    {
        var builder = _core.MenusAPI.CreateBuilder().Design
            .SetMenuTitle(player.Localizer["cells_submenu.title"]);

        var openCells = new ButtonMenuOption(player.Localizer["cells_submenu_option.open_cells"]);
        openCells.Click += (sender, args) =>
        {
            _core.Scheduler.NextWorldUpdate(() =>
            {
                if (_cellManager.CellsOpen)
                    return;

                _cellManager.OpenCells();
                _players.SendMessage(MessageType.Chat, "cells_opened_warden");
            });
            return ValueTask.CompletedTask;
        };

        var closeCells = new ButtonMenuOption(player.Localizer["cells_submenu_option.close_cells"]);
        closeCells.Click += (sender, args) =>
        {
            _core.Scheduler.NextWorldUpdate(() =>
            {
                if (!_cellManager.CellsOpen)
                    return;

                _cellManager.CloseCells();
                _players.SendMessage(MessageType.Chat, "cells_closed_warden");
            });
            return ValueTask.CompletedTask;
        };

        builder.AddOption(openCells);
        builder.AddOption(closeCells);

        return builder.Build();
    }

}