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



        builder.AddOption(toggleCells);
        builder.AddOption(toggleBox);

        var built = builder.Build();
        _core.MenusAPI.OpenMenuForPlayer(player.Player, built);
        
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