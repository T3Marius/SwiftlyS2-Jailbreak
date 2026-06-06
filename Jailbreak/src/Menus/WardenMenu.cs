using Jailbreak.Contract;
using SwiftlyS2.Core.Menus.OptionsBase;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Menus;
using SwiftlyS2.Shared.Natives;
using SwiftlyS2.Shared.Players;

namespace Jailbreak;

public sealed class WardenMenu
{
    private readonly ISwiftlyCore _core;
    private readonly CellManager _cellManager;
    private readonly BoxManager _boxManager;
    private readonly IJBPlayerManagement _players;
    private readonly WardenDatabase _wardenDatabase;
    private readonly SpecialDayManager _specialDayManager;

    private static readonly ColorChoice[] ColorChoices =
    [
        new("visual_color.red", new Color(255, 40, 40, 230)),
        new("visual_color.blue", new Color(80, 170, 255, 230)),
        new("visual_color.green", new Color(70, 255, 110, 230)),
        new("visual_color.yellow", new Color(255, 220, 60, 230)),
        new("visual_color.purple", new Color(190, 90, 255, 230)),
        new("visual_color.cyan", new Color(80, 255, 255, 230)),
        new("visual_color.white", new Color(255, 255, 255, 230))
    ];

    public WardenMenu(
        ISwiftlyCore core,
        CellManager cellManager,
        BoxManager boxManager,
        IJBPlayerManagement players,
        WardenDatabase wardenDatabase,
        SpecialDayManager specialDayManager)
    {
        _core = core;
        _cellManager = cellManager;
        _boxManager = boxManager;
        _players = players;
        _wardenDatabase = wardenDatabase;
        _specialDayManager = specialDayManager;
    }

    // ─── Root ────────────────────────────────────────────────────────────────

    public void Show(IJBPlayer player)
    {
        if (BlockDuringSpecialDay(player))
            return;

        var builder = CreateBuilder(player, "warden_menu.title");

        AddSubmenu(builder, player, "warden_menu_option.toggle_cells", () => CellsSubmenu(player));
        AddSubmenu(builder, player, "warden_menu_option.toggle_box", () => BoxSubmenu(player));
        AddSubmenu(builder, player, "warden_menu_option.toggle_voice", () => VoiceSubmenu(player));
        AddSubmenu(builder, player, "warden_menu_option.manage_deputy", () => DeputySubmenu(player));
        AddSubmenu(builder, player, "warden_menu_option.manage_freeday", () => FreedaySubmenu(player));
        AddSubmenu(builder, player, "warden_menu_option.special_days", () => SpecialDaysSubmenu(player));
        AddSubmenu(builder, player, "warden_menu_option.visual_management", () => VisualManagementSubmenu(player));

        _core.MenusAPI.OpenMenuForPlayer(player.Player, builder.Build());
    }

    public void ShowSpecialDays(IJBPlayer player)
    {
        if (BlockDuringSpecialDay(player))
            return;

        _core.MenusAPI.OpenMenuForPlayer(player.Player, SpecialDaysSubmenu(player));
    }

    private IMenuAPI SpecialDaysSubmenu(IJBPlayer player)
    {
        var builder = CreateBuilder(player, "special_days_submenu.title");

        AddSubmenu(builder, player, "special_days_submenu_option.days", () => SpecialDaysListSubmenu(player));

        return builder.Build();
    }

    private IMenuAPI SpecialDaysListSubmenu(IJBPlayer player)
    {
        var builder = CreateBuilder(player, "special_days_days_submenu.title");
        var days = _specialDayManager.SpecialDays.OrderBy(day => day.Name).ToList();

        if (days.Count == 0)
        {
            builder.AddOption(new TextMenuOption(player.Localizer["special_days_submenu_option.no_days"]));
            return builder.Build();
        }

        foreach (var day in days)
        {
            AddButton(builder, day.Name, () =>
            {
                _core.Scheduler.NextWorldUpdate(() =>
                {
                    if (BlockDuringSpecialDay(player))
                        return;

                    if (_specialDayManager.QueuedSpecialDay != null)
                    {
                        player.SendMessage(MessageType.Chat, "special_day_already_queued", true, args: _specialDayManager.QueuedSpecialDay.Name);
                        return;
                    }

                    if (_specialDayManager.CooldownRoundsRemaining > 0)
                    {
                        player.SendMessage(MessageType.Chat, "special_day_cooldown", true, args: _specialDayManager.CooldownRoundsRemaining);
                        return;
                    }

                    if (!_specialDayManager.QueueSpecialDay(day.Id))
                        player.SendMessage(MessageType.Chat, "special_day_cannot_queue", true, args: day.Name);
                });
            });
        }

        return builder.Build();
    }

    private IMenuAPI VisualManagementSubmenu(IJBPlayer player)
    {
        var builder = CreateBuilder(player, "visual_management_submenu.title");

        AddDynamicSubmenu(
            builder,
            () => player.Localizer["visual_management_submenu_option.laser_color_current", GetSelectedLaserColorName(player)],
            () => LaserColorSubmenu(player));

        AddDynamicSubmenu(
            builder,
            () => player.Localizer["visual_management_submenu_option.ping_color_current", GetSelectedPingColorName(player)],
            () => PingColorSubmenu(player));

        return builder.Build();
    }

    private IMenuAPI LaserColorSubmenu(IJBPlayer player)
    {
        var builder = CreateBuilder(player, "laser_color_submenu.title");

        AddColorOptions(
            builder,
            player,
            settings => settings.LaserRainbow,
            settings => settings.LaserColor,
            saveRainbow: () => _wardenDatabase.SaveWardenLaserRainbow(player.SteamID),
            saveColor: color => _wardenDatabase.SaveWardenLaserColor(player.SteamID, color));

        return builder.Build();
    }

    private IMenuAPI PingColorSubmenu(IJBPlayer player)
    {
        var builder = CreateBuilder(player, "ping_color_submenu.title");

        AddColorOptions(
            builder,
            player,
            settings => settings.BeamRainbow,
            settings => settings.BeamColor,
            saveRainbow: () => _wardenDatabase.SaveWardenBeamRainbow(player.SteamID),
            saveColor: color => _wardenDatabase.SaveWardenBeamColor(player.SteamID, color));

        return builder.Build();
    }

    private void AddColorOptions(
        IMenuBuilderAPI builder,
        IJBPlayer player,
        Func<WardenDatabase.WardenVisualSettings, bool> isRainbowSelected,
        Func<WardenDatabase.WardenVisualSettings, Color> selectedColor,
        Action saveRainbow,
        Action<Color> saveColor)
    {
        AddDynamicButton(builder, () =>
        {
            var label = player.Localizer["visual_color.rainbow"];
            return SelectedLabel(label, isRainbowSelected(_wardenDatabase.GetWardenSettings(player.SteamID)));
        }, () =>
        {
            _core.Scheduler.NextWorldUpdate(() =>
            {
                saveRainbow();
                player.SendMessage(MessageType.Chat, "visual_color_selected", true, args: player.Localizer["visual_color.rainbow"]);
            });
        });

        foreach (var choice in ColorChoices)
        {
            AddDynamicButton(builder, () =>
            {
                var settings = _wardenDatabase.GetWardenSettings(player.SteamID);
                var label = player.Localizer[choice.LocalizerKey];
                var selected = !isRainbowSelected(settings) && ColorsEqual(selectedColor(settings), choice.Color);
                return SelectedLabel(label, selected);
            }, () =>
            {
                _core.Scheduler.NextWorldUpdate(() =>
                {
                    saveColor(choice.Color);
                    player.SendMessage(MessageType.Chat, "visual_color_selected", true, args: player.Localizer[choice.LocalizerKey]);
                });
            });
        }
    }

    private IMenuAPI FreedaySubmenu(IJBPlayer player)
    {
        var builder = CreateBuilder(player, "freeday_submenu.title");

        AddSubmenu(builder, player, "freeday_submenu_option.give_freeday", () => GiveFreedaySubmenu(player));
        AddSubmenu(builder, player, "freeday_submenu_option.remove_freeday", () => RemoveFreedaySubmenu(player));

        return builder.Build();
    }

    private IMenuAPI GiveFreedaySubmenu(IJBPlayer player)
    {
        var builder = CreateBuilder(player, "give_freeday_submenu.title");

        var prisoners = _players.GetPlayersByTeam(JBTeam.Prisoner).ToList();

        foreach (var prisoner in prisoners)
        {
            AddDynamicButton(builder, () =>
            {
                var state = player.Localizer[prisoner.IsFreeday ? "menu_state.freeday" : "menu_state.none"];
                return player.Localizer["freeday_submenu_option.prisoner_state_label", prisoner.Player.Name, state];
            }, () =>
            {
                _core.Scheduler.NextWorldUpdate(() =>
                {
                    if (BlockDuringSpecialDay(player))
                        return;

                    if (prisoner.IsFreeday)
                        return;

                    prisoner.SetFreeday(true);
                    _players.SendMessage(MessageType.Chat, "freeday_given", true, 0, prisoner.Player.Name);
                });
            });
        }

        return builder.Build();
    }

    private IMenuAPI RemoveFreedaySubmenu(IJBPlayer player)
    {
        var builder = CreateBuilder(player, "remove_freeday_submenu.title");

        var prisoners = _players.GetPlayersByTeam(JBTeam.Prisoner).ToList();
        foreach (var prisoner in prisoners)
        {
            AddDynamicButton(builder, () =>
            {
                var state = player.Localizer[prisoner.IsFreeday ? "menu_state.freeday" : "menu_state.none"];
                return player.Localizer["freeday_submenu_option.prisoner_state_label", prisoner.Player.Name, state];
            }, () =>
            {
                _core.Scheduler.NextWorldUpdate(() =>
                {
                    if (BlockDuringSpecialDay(player))
                        return;

                    if (!prisoner.IsFreeday)
                        return;

                    prisoner.SetFreeday(false);
                    _players.SendMessage(MessageType.Chat, "freeday_removed", true, 0, prisoner.Player.Name);
                });
            });
        } 

        return builder.Build();
    }

    // ─── Cells ───────────────────────────────────────────────────────────────

    private IMenuAPI CellsSubmenu(IJBPlayer player)
    {
        var builder = CreateBuilder(player, "cells_submenu.title");

        AddDynamicButton(builder, () =>
        {
            var state = player.Localizer[_cellManager.CellsOpen ? "menu_state.open" : "menu_state.closed"];
            return player.Localizer["cells_submenu_option.toggle_cells", state];
        }, () =>
        {
            _core.Scheduler.NextWorldUpdate(() =>
            {
                if (BlockDuringSpecialDay(player))
                    return;

                if (_cellManager.CellsOpen)
                {
                    _cellManager.CloseCells();
                    _players.SendMessage(MessageType.Chat, "cells_closed_sender", true, args: player.Player.Name);
                }
                else
                {
                    _cellManager.OpenCells();
                    _players.SendMessage(MessageType.Chat, "cells_opened_sender", true, args: player.Player.Name);
                }
            });
        });

        return builder.Build();
    }

    // ─── Box ─────────────────────────────────────────────────────────────────

    private IMenuAPI BoxSubmenu(IJBPlayer player)
    {
        var builder = CreateBuilder(player, "box_submenu.title");

        AddDynamicButton(builder, () =>
        {
            var state = player.Localizer[_boxManager.BoxEnabled ? "menu_state.enabled" : "menu_state.disabled"];
            return player.Localizer["box_submenu_option.toggle_box", state];
        }, () =>
        {
            _core.Scheduler.NextWorldUpdate(() =>
            {
                if (BlockDuringSpecialDay(player))
                    return;

                if (_boxManager.BoxEnabled)
                {
                    _boxManager.StopBox();
                    _players.SendMessage(MessageType.Chat, "box_disabled");
                }
                else
                {
                    _boxManager.StartBox();
                    _players.SendMessage(MessageType.Chat, "box_enabled");
                }
            });
        });

        return builder.Build();
    }

    // ─── Voice ───────────────────────────────────────────────────────────────

    private IMenuAPI VoiceSubmenu(IJBPlayer player)
    {
        var builder = CreateBuilder(player, "voice_submenu.title");

        // Per-prisoner submenus listed first for quick access
        foreach (var prisoner in _players.GetPlayersByTeam(JBTeam.Prisoner))
        {
            AddDynamicSubmenu(
                builder,
                () =>
                {
                    var state = player.Localizer[prisoner.IsMuted ? "menu_state.muted" : "menu_state.unmuted"];
                    return player.Localizer["voice_submenu_option.prisoner_state_label", prisoner.Player.Name, state];
                },
                () => PrisonerVoiceSubmenu(player, prisoner));
        }

        // Bulk actions at the bottom
        AddButton(builder, player.Localizer["voice_submenu_option.unmute_all_prisoner"], () =>
        {
            _core.Scheduler.NextWorldUpdate(() =>
            {
                if (BlockDuringSpecialDay(player))
                    return;

                foreach (var p in _players.GetPlayersByTeam(JBTeam.Prisoner))
                {
                    p.Unmute();
                    p.WasUnmutedByWarden = true;
                }
                _players.SendMessage(MessageType.Chat, "all_prisoners_unmuted_warden", true);
            });
        });

        AddButton(builder, player.Localizer["voice_submenu_option.mute_all_prisoner"], () =>
        {
            _core.Scheduler.NextWorldUpdate(() =>
            {
                if (BlockDuringSpecialDay(player))
                    return;

                foreach (var p in _players.GetPlayersByTeam(JBTeam.Prisoner))
                {
                    p.Mute();
                    p.WasUnmutedByWarden = false;
                }
                _players.SendMessage(MessageType.Chat, "all_prisoners_muted_warden", true);
            });
        });

        return builder.Build();
    }

    private IMenuAPI PrisonerVoiceSubmenu(IJBPlayer player, IJBPlayer prisoner)
    {
        var builder = _core.MenusAPI.CreateBuilder().Design
            .SetMenuTitle(prisoner.Player.Name);

        AddDynamicButton(builder, () =>
        {
            var state = player.Localizer[prisoner.IsMuted ? "menu_state.muted" : "menu_state.unmuted"];
            return player.Localizer["voice_submenu_option.toggle_prisoner", state];
        }, () =>
        {
            _core.Scheduler.NextWorldUpdate(() =>
            {
                if (BlockDuringSpecialDay(player))
                    return;

                if (prisoner.IsMuted)
                {
                    prisoner.Unmute();
                    prisoner.WasUnmutedByWarden = true;
                    _players.SendMessage(MessageType.Chat, "prisoner_unmuted_warden", true, args: prisoner.Player.Name);
                }
                else
                {
                    prisoner.Mute();
                    prisoner.WasUnmutedByWarden = false;
                    _players.SendMessage(MessageType.Chat, "prisoner_muted_warden", true, args: prisoner.Player.Name);
                }
            });
        });

        return builder.Build();
    }

    // ─── Deputy ──────────────────────────────────────────────────────────────

    private IMenuAPI DeputySubmenu(IJBPlayer player)
    {
        var builder = CreateBuilder(player, "manage_deputy_submenu.title");

        var currentDeputy = _players.GetDeputy();

        // Informational label showing who (if anyone) is currently deputy
        var statusLabel = currentDeputy != null
            ? player.Localizer["manage_deputy_submenu_option.current_deputy", currentDeputy.Player.Name]
            : player.Localizer["manage_deputy_submenu_option.current_deputy_none"];

        builder.AddOption(new TextMenuOption(statusLabel));

        // Remove deputy — only actionable when one exists
        AddButton(builder, player.Localizer["manage_deputy_submenu_option.remove_deputy"], () =>
        {
            _core.Scheduler.NextWorldUpdate(() =>
            {
                if (BlockDuringSpecialDay(player))
                    return;

                var deputy = _players.GetDeputy();
                if (deputy == null) return;

                deputy.SetDeputy(false);
                _players.SendMessage(MessageType.Chat, "deputy_removed_warden", true, args: deputy.Player.Name);
            });
        });

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
                AddButton(builder, guardRef.Player.Name, () =>
                {
                    _core.Scheduler.NextWorldUpdate(() =>
                    {
                        if (BlockDuringSpecialDay(player))
                            return;

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
                });
            }
        }

        return builder.Build();
    }

    private IMenuBuilderAPI CreateBuilder(IJBPlayer player, string titleKey)
    {
        return _core.MenusAPI.CreateBuilder().Design
            .SetMenuTitle(player.Localizer[titleKey]);
    }

    private bool BlockDuringSpecialDay(IJBPlayer player)
    {
        if (!_specialDayManager.IsSpecialDayActive)
            return false;

        player.SendMessage(MessageType.Chat, "special_day_active_blocked", true);
        return true;
    }

    private static void AddSubmenu(IMenuBuilderAPI builder, IJBPlayer player, string labelKey, Func<IMenuAPI> submenu)
    {
        builder.AddOption(new SubmenuMenuOption(player.Localizer[labelKey], submenu));
    }

    private static void AddDynamicSubmenu(IMenuBuilderAPI builder, Func<string> label, Func<IMenuAPI> submenu)
    {
        var option = new SubmenuMenuOption(label(), submenu)
        {
            BindingText = label
        };
        builder.AddOption(option);
    }

    private static void AddButton(IMenuBuilderAPI builder, string label, Action action)
    {
        var option = new ButtonMenuOption(label);
        option.Click += (_, _) =>
        {
            action();
            return ValueTask.CompletedTask;
        };
        builder.AddOption(option);
    }

    private static void AddDynamicButton(IMenuBuilderAPI builder, Func<string> label, Action action)
    {
        var option = new ButtonMenuOption(label(), 120, 0)
        {
            BindingText = label
        };
        option.Click += (_, _) =>
        {
            action();
            return ValueTask.CompletedTask;
        };
        builder.AddOption(option);
    }

    private string GetSelectedLaserColorName(IJBPlayer player)
    {
        var settings = _wardenDatabase.GetWardenSettings(player.SteamID);
        return GetSelectedColorName(player, settings.LaserRainbow, settings.LaserColor);
    }

    private string GetSelectedPingColorName(IJBPlayer player)
    {
        var settings = _wardenDatabase.GetWardenSettings(player.SteamID);
        return GetSelectedColorName(player, settings.BeamRainbow, settings.BeamColor);
    }

    private static string GetSelectedColorName(IJBPlayer player, bool rainbow, Color color)
    {
        if (rainbow)
            return player.Localizer["visual_color.rainbow"];

        var match = ColorChoices.FirstOrDefault(choice => ColorsEqual(choice.Color, color));
        return match != null
            ? player.Localizer[match.LocalizerKey]
            : player.Localizer["visual_color.custom"];
    }

    private static string SelectedLabel(string label, bool selected)
    {
        return selected ? $"[lime]Selected[silver] - {label}" : label;
    }

    private static bool ColorsEqual(Color left, Color right)
    {
        return left.R == right.R
            && left.G == right.G
            && left.B == right.B
            && left.A == right.A;
    }

    private sealed record ColorChoice(string LocalizerKey, Color Color);
}
