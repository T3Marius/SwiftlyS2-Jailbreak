using Jailbreak.Contract;
using Microsoft.Extensions.Options;
using SwiftlyS2.Shared;
using T3Menu.Contract;

namespace Jailbreak;

public sealed class JBStats
{
    private readonly ISwiftlyCore _core;
    private readonly JBStatsDB _database;
    private readonly JBStatsConfig _config;

    public JBStats(ISwiftlyCore core, JBStatsDB database, IOptions<JBStatsConfig> config)
    {
        _core = core;
        _database = database;
        _config = config.Value;
    }

    public void Show(IJBPlayer player)
    {
        var menu = CreateMenu(player, "jbstats_menu.title");

        AddSubmenu(menu, player, "jbstats_menu_option.last_request", () => LastRequestMenu(player));
        AddSubmenu(menu, player, "jbstats_menu_option.special_days", () => SpecialDaysMenu(player));

        menu.Open(player.Player);
    }

    private Menu LastRequestMenu(IJBPlayer player)
    {
        var menu = CreateMenu(player, "jbstats_lr_menu.title");

        AddSubmenu(menu, player, "jbstats_lr_menu_option.top", () => LastRequestTopMenu(player));
        AddSubmenu(menu, player, "jbstats_lr_menu_option.your_stats", () => LastRequestYourStatsMenu(player));

        return menu;
    }

    private Menu LastRequestTopMenu(IJBPlayer player)
    {
        var menu = CreateMenu(player, "jbstats_lr_top_menu.title");
        var top = _database.GetTopLastRequestPlayers(_config.TopLimit);

        if (top.Count == 0)
        {
            menu.AddSpacer(player.Localizer["jbstats_lr_top_menu.empty"]);
            return menu;
        }

        var rank = 1;

        foreach (var record in top)
        {
            var name = string.IsNullOrWhiteSpace(record.PlayerName)
                ? record.SteamId.ToString()
                : record.PlayerName;

            menu.AddSpacer(
                player.Localizer[
                    "jbstats_lr_top_menu.row",
                    rank,
                    name,
                    record.LastRequestWins,
                    record.LastRequestLosses
                ]
            );

            rank++;
        }

        return menu;
    }

    private Menu LastRequestYourStatsMenu(IJBPlayer player)
    {
        var menu = CreateMenu(player, "jbstats_lr_your_menu.title");
        var stats = _database.GetPlayerStats(player.SteamID, player.Player.Name);

        menu.AddSpacer(
            player.Localizer[
                "jbstats_lr_your_menu.player",
                player.Player.Name
            ]
        );

        menu.AddSpacer(
            player.Localizer[
                "jbstats_lr_your_menu.wins",
                stats.LastRequestWins
            ]
        );

        menu.AddSpacer(
            player.Localizer[
                "jbstats_lr_your_menu.losses",
                stats.LastRequestLosses
            ]
        );

        menu.AddSpacer(
            player.Localizer[
                "jbstats_lr_your_menu.ratio",
                FormatRatio(stats)
            ]
        );

        return menu;
    }

    private Menu SpecialDaysMenu(IJBPlayer player)
    {
        var menu = CreateMenu(player, "jbstats_sd_menu.title");

        AddSubmenu(menu, player, "jbstats_sd_menu_option.top", () => SpecialDaysTopMenu(player));
        AddSubmenu(menu, player, "jbstats_sd_menu_option.your_stats", () => SpecialDaysYourStatsMenu(player));

        return menu;
    }

    private Menu SpecialDaysTopMenu(IJBPlayer player)
    {
        var menu = CreateMenu(player, "jbstats_sd_top_menu.title");
        var top = _database.GetTopSpecialDayPlayers(_config.TopLimit);

        if (top.Count == 0)
        {
            menu.AddSpacer(player.Localizer["jbstats_sd_top_menu.empty"]);
            return menu;
        }

        var rank = 1;

        foreach (var record in top)
        {
            var name = string.IsNullOrWhiteSpace(record.PlayerName)
                ? record.SteamId.ToString()
                : record.PlayerName;

            menu.AddSpacer(
                player.Localizer[
                    "jbstats_sd_top_menu.row",
                    rank,
                    name,
                    record.SpecialDayWins,
                    record.SpecialDayLosses
                ]
            );

            rank++;
        }

        return menu;
    }

    private Menu SpecialDaysYourStatsMenu(IJBPlayer player)
    {
        var menu = CreateMenu(player, "jbstats_sd_your_menu.title");
        var stats = _database.GetPlayerStats(player.SteamID, player.Player.Name);

        menu.AddSpacer(
            player.Localizer[
                "jbstats_sd_your_menu.player",
                player.Player.Name
            ]
        );

        menu.AddSpacer(
            player.Localizer[
                "jbstats_sd_your_menu.wins",
                stats.SpecialDayWins
            ]
        );

        menu.AddSpacer(
            player.Localizer[
                "jbstats_sd_your_menu.losses",
                stats.SpecialDayLosses
            ]
        );

        menu.AddSpacer(
            player.Localizer[
                "jbstats_sd_your_menu.ratio",
                FormatSpecialDayRatio(stats)
            ]
        );

        return menu;
    }

    private static Menu CreateMenu(IJBPlayer player, string titleKey)
    {
        return new Menu(player.Localizer[titleKey])
        {
            HasExitButton = true,
        };
    }

    private static void AddSubmenu(
        Menu menu,
        IJBPlayer player,
        string labelKey,
        Func<Menu> submenu)
    {
        menu.AddSubmenu(
            player.Localizer[labelKey],
            submenu
        );
    }

    private static string FormatRatio(JBStatsDB.JBStatsRecord stats)
    {
        if (stats.LastRequestLosses <= 0)
            return stats.LastRequestWins.ToString("0.00");

        return ((double)stats.LastRequestWins / stats.LastRequestLosses)
            .ToString("0.00");
    }

    private static string FormatSpecialDayRatio(JBStatsDB.JBStatsRecord stats)
    {
        if (stats.SpecialDayLosses <= 0)
            return stats.SpecialDayWins.ToString("0.00");

        return ((double)stats.SpecialDayWins / stats.SpecialDayLosses)
            .ToString("0.00");
    }
}
