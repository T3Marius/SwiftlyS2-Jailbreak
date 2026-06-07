using Jailbreak.Contract;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.EntitySystem;
using SwiftlyS2.Shared.Natives;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.SchemaDefinitions;

namespace Jailbreak;

public sealed class WardenCommands
{
    private readonly ISwiftlyCore            _core;
    private readonly WardenConfig            _config = new();
    private readonly IJBPlayerManagement     _players;
    private readonly WardenMenu              _wardenMenu;
    private readonly BoxManager              _boxManager;
    private readonly CellManager             _cellManager;
    private readonly CuffsManager            _cuffsManager;
    private readonly DrawManager             _drawManager;
    private readonly SpecialDayManager       _specialDayManager;
    private readonly LastRequestManager      _lastRequestManager;
    private readonly ILogger<WardenCommands> _log;

    public WardenCommands(
        ISwiftlyCore core,
        IOptions<WardenConfig> config, 
        IJBPlayerManagement players, 
        WardenMenu wardenMenu, 
        BoxManager boxManager,
        CellManager cellManager,
        CuffsManager cuffsManager,
        DrawManager drawManager,
        SpecialDayManager specialDayManager,
        LastRequestManager lastRequestManager,
        ILogger<WardenCommands> log)
    {
        _core    = core;
        _config  = config.Value;
        _players = players;
        _wardenMenu = wardenMenu;
        _boxManager = boxManager;
        _cellManager = cellManager;
        _cuffsManager = cuffsManager;
        _drawManager = drawManager;
        _specialDayManager = specialDayManager;
        _lastRequestManager = lastRequestManager;
        _log     = log;
    }

    public void Register()
    {
        foreach (var cmd in _config.Commands.BecomeWarden)
        {
            if (_core.Command.IsCommandRegistered(cmd))
                continue;
    
            _core.Command.RegisterCommand(cmd, BecomeWarden);
        }

        foreach (var cmd in _config.Commands.GiveUpWarden)
        {
            if (_core.Command.IsCommandRegistered(cmd))
                continue;

            _core.Command.RegisterCommand(cmd, GiveUpWarden);
        }

        foreach (var cmd in _config.Commands.WardenHelp)
        {
            if (_core.Command.IsCommandRegistered(cmd))
                continue;

            _core.Command.RegisterCommand(cmd, WardenHelp);
        }

        foreach (var cmd in _config.Commands.WardenMenu)
        {
            if (_core.Command.IsCommandRegistered(cmd))
                continue;

            _core.Command.RegisterCommand(cmd, WardenMenu);
        }

        foreach (var cmd in _config.Commands.SpecialDays)
        {
            if (_core.Command.IsCommandRegistered(cmd))
                continue;

            _core.Command.RegisterCommand(cmd, SpecialDays);
        }

        foreach (var cmd in _config.Commands.ToggleBox)
        {
            if (_core.Command.IsCommandRegistered(cmd))
                continue;

            _core.Command.RegisterCommand(cmd, ToggleBox);
        }

        foreach (var cmd in _config.Commands.ToggleCells)
        {
            if (_core.Command.IsCommandRegistered(cmd))
                continue;

            _core.Command.RegisterCommand(cmd, ToggleCells);
        }

        foreach (var cmd in _config.Commands.ToggleDraw)
        {
            if (_core.Command.IsCommandRegistered(cmd))
                continue;

            _core.Command.RegisterCommand(cmd, ToggleDraw);
        }

        foreach (var cmd in _config.Commands.DrawColor)
        {
            if (_core.Command.IsCommandRegistered(cmd))
                continue;

            _core.Command.RegisterCommand(cmd, DrawColor);
        }

        foreach (var cmd in _config.Commands.DrawClear)
        {
            if (_core.Command.IsCommandRegistered(cmd))
                continue;

            _core.Command.RegisterCommand(cmd, DrawClear);
        }

    }
    public void Unregister()
    {
        foreach (var cmd in _config.Commands.BecomeWarden)
        {
            if (!_core.Command.IsCommandRegistered(cmd))
                continue;

            _core.Command.UnregisterCommand(cmd);
        }

        foreach (var cmd in _config.Commands.GiveUpWarden)
        {
            if (!_core.Command.IsCommandRegistered(cmd))
                continue;

            _core.Command.UnregisterCommand(cmd);
        }

        foreach (var cmd in _config.Commands.WardenHelp)
        {
            if (!_core.Command.IsCommandRegistered(cmd))
                continue;

            _core.Command.UnregisterCommand(cmd);
        }

        foreach (var cmd in _config.Commands.WardenMenu)
        {
            if (!_core.Command.IsCommandRegistered(cmd))
                continue;

            _core.Command.UnregisterCommand(cmd);
        }

        foreach (var cmd in _config.Commands.SpecialDays)
        {
            if (!_core.Command.IsCommandRegistered(cmd))
                continue;

            _core.Command.UnregisterCommand(cmd);
        }

        foreach (var cmd in _config.Commands.ToggleBox)
        {
            if (!_core.Command.IsCommandRegistered(cmd))
                continue;

            _core.Command.UnregisterCommand(cmd);
        }

        foreach (var cmd in _config.Commands.ToggleCells)
        {
            if (!_core.Command.IsCommandRegistered(cmd))
                continue;

            _core.Command.UnregisterCommand(cmd);
        }

        foreach (var cmd in _config.Commands.ToggleDraw)
        {
            if (!_core.Command.IsCommandRegistered(cmd))
                continue;

            _core.Command.UnregisterCommand(cmd);
        }

        foreach (var cmd in _config.Commands.DrawColor)
        {
            if (!_core.Command.IsCommandRegistered(cmd))
                continue;

            _core.Command.UnregisterCommand(cmd);
        }

        foreach (var cmd in _config.Commands.DrawClear)
        {
            if (!_core.Command.IsCommandRegistered(cmd))
                continue;

            _core.Command.UnregisterCommand(cmd);
        }

    }

    private void BecomeWarden(ICommandContext ctx)
    {
        if (ctx.Sender == null)
            return;
        
        var player = _players.SyncPlayer(ctx.Sender);
        if (player == null)
            return;

        if (BlockDuringSpecialDay(player))
            return;

        if (player.Team != JBTeam.Guard)
        {
            player.SendMessage(MessageType.Chat, "warden_not_guard", true);
            return;
        }

        if (!player.CanBecomeWarden)
        {
            player.SendMessage(MessageType.Chat, "cannot_become_warden", true);
            return;
        }

        if (player.IsWarden)
        {
            player.SendMessage(MessageType.Chat, "you_already_warden", true);
            return;
        }

        if (_players.GetWarden() != null)
        {
            player.SendMessage(MessageType.Chat, "warden_already_exists", true);
            return;
        }

        player.SetWarden(true);
        if (player.IsWarden)
        {
            _cuffsManager.OnWardenGive(player);
            player.SendMessage(MessageType.Chat, "you_are_new_warden", true);
        }
    }
    private void GiveUpWarden(ICommandContext ctx)
    {
        if (ctx.Sender == null)
            return;
        
        var player = _players.SyncPlayer(ctx.Sender);
        if (player == null)
            return;

        if (BlockDuringSpecialDay(player))
            return;

        if (!player.IsWarden)
        {
            player.SendMessage(MessageType.Chat, "you_are_not_warden", true);
            return;
        }

        _cuffsManager.OnWardenRemove(player);
        player.SetWarden(false, "giveup");
    }
    private void WardenHelp(ICommandContext ctx)
    {
        if (ctx.Sender == null)
            return;
        
        var player = _players.SyncPlayer(ctx.Sender);
        if (player == null)
            return;

        if (BlockDuringSpecialDay(player))
            return;

        if (!player.IsWarden)
        {
            player.SendMessage(MessageType.Chat, "you_are_not_warden", true);
            return;
        }

        player.SendMessage(MessageType.Chat, "warden_help.header", true);
        player.SendMessage(MessageType.Chat, "warden_help.become", true);
        player.SendMessage(MessageType.Chat, "warden_help.giveup", true);
        player.SendMessage(MessageType.Chat, "warden_help.menu"  , true);
        player.SendMessage(MessageType.Chat, "warden_help.sd"    , true);
        player.SendMessage(MessageType.Chat, "warden_help.box"   , true);
    }
    private void WardenMenu(ICommandContext ctx)
    {
        if (ctx.Sender == null)
            return;
        
        var player = _players.SyncPlayer(ctx.Sender);
        if (player == null)
            return;

        if (BlockDuringSpecialDay(player))
            return;

        if (!player.IsWarden)
        {
            player.SendMessage(MessageType.Chat, "you_are_not_warden", true);
            return;
        }

        _wardenMenu.Show(player);
    }
    private void SpecialDays(ICommandContext ctx)
    {
        if (ctx.Sender == null)
            return;

        var player = _players.SyncPlayer(ctx.Sender);
        if (player == null)
            return;

        if (BlockDuringSpecialDay(player))
            return;

        if (!player.IsWarden)
        {
            player.SendMessage(MessageType.Chat, "you_are_not_warden", true);
            return;
        }

        _wardenMenu.ShowSpecialDays(player);
    }
    private void ToggleBox(ICommandContext ctx)
    {
        if (ctx.Sender == null)
            return;
        
        var player = _players.SyncPlayer(ctx.Sender);
        if (player == null)
            return;

        if (BlockDuringSpecialDay(player))
            return;

        if (!player.IsWarden)
        {
            player.SendMessage(MessageType.Chat, "you_are_not_warden", true);
            return;
        }

        _boxManager.BoxEnabled = !_boxManager.BoxEnabled;
        if (_boxManager.BoxEnabled)
        {
            _boxManager.StartBox();
            _players.SendMessage(MessageType.Chat, "box_enabled", true);
        }
        else
        {
            _boxManager.StopBox();
            _players.SendMessage(MessageType.Chat, "box_disabled", true);

        }
    }
    private void ToggleCells(ICommandContext ctx)
    {
        if (ctx.Sender == null)
            return;
        
        var player = _players.SyncPlayer(ctx.Sender);
        if (player == null)
            return;

        if (BlockDuringSpecialDay(player))
            return;

        if (!player.IsWarden && !player.IsDeputy)
        {
            player.SendMessage(MessageType.Chat, "you_are_not_warden&deputy", true);
            return;
        }

        _cellManager.CellsOpen = !_cellManager.CellsOpen;

        string sender = player.IsDeputy
            ? "Deputy"
            : "Warden";

        if (_cellManager.CellsOpen)
        {
            _cellManager.OpenCells();
            _players.SendMessage(MessageType.Chat, "cells_opened_sender", true, args: sender);
        }
        else
        {
            _cellManager.CloseCells();
            _players.SendMessage(MessageType.Chat, "cells_closed_sender", true, args: sender);
        }
    }

    private void ToggleDraw(ICommandContext ctx)
    {
        if (ctx.Sender == null)
            return;

        var player = _players.SyncPlayer(ctx.Sender);
        if (player == null)
            return;

        if (BlockDuringSpecialDay(player))
            return;

        if (!player.IsWarden)
        {
            player.SendMessage(MessageType.Chat, "you_are_not_warden", true);
            return;
        }

        var enabled = _drawManager.ToggleDrawing(player);
        player.SendMessage(MessageType.Chat, enabled ? "draw_enabled" : "draw_disabled", true);
    }

    private void DrawColor(ICommandContext ctx)
    {
        if (ctx.Sender == null)
            return;

        var player = _players.SyncPlayer(ctx.Sender);
        if (player == null)
            return;

        if (BlockDuringSpecialDay(player))
            return;

        if (!player.IsWarden)
        {
            player.SendMessage(MessageType.Chat, "you_are_not_warden", true);
            return;
        }

        _wardenMenu.ShowDrawColor(player);
    }

    private void DrawClear(ICommandContext ctx)
    {
        if (ctx.Sender == null)
            return;

        var player = _players.SyncPlayer(ctx.Sender);
        if (player == null)
            return;

        if (BlockDuringSpecialDay(player))
            return;

        if (!player.IsWarden)
        {
            player.SendMessage(MessageType.Chat, "you_are_not_warden", true);
            return;
        }

        _drawManager.ClearDrawing(player);
        player.SendMessage(MessageType.Chat, "draw_cleared", true);
    }

    private bool BlockDuringSpecialDay(IJBPlayer player)
    {
        if (_specialDayManager.IsSpecialDayActive)
        {
            player.SendMessage(MessageType.Chat, "special_day_active_blocked", true);
            return true;
        }

        if (_lastRequestManager.IsLastRequestActive)
        {
            player.SendMessage(MessageType.Chat, "last_request_already_active", true);
            return true;
        }

        return false;
    }

}
