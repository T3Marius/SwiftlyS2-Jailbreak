using Jailbreak.Contract;
using Microsoft.Extensions.Options;
using SwiftlyS2.Core.Menus.OptionsBase;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Menus;

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
        var builder = CreateBuilder(player, "jbstats_menu.title");

        AddSubmenu(builder, player, "jbstats_menu_option.last_request", () => LastRequestMenu(player));
        AddSubmenu(builder, player, "jbstats_menu_option.special_days", () => SpecialDaysMenu(player));

        _core.MenusAPI.OpenMenuForPlayer(player.Player, builder.Build());
    }

    private IMenuAPI LastRequestMenu(IJBPlayer player)
    {
        var builder = CreateBuilder(player, "jbstats_lr_menu.title");

        AddSubmenu(builder, player, "jbstats_lr_menu_option.top", () => LastRequestTopMenu(player));
        AddSubmenu(builder, player, "jbstats_lr_menu_option.your_stats", () => LastRequestYourStatsMenu(player));

        return builder.Build();
    }

    private IMenuAPI LastRequestTopMenu(IJBPlayer player)
    {
        var builder = CreateBuilder(player, "jbstats_lr_top_menu.title");
        var top = _database.GetTopLastRequestPlayers(_config.TopLimit);

        if (top.Count == 0)
        {
            builder.AddOption(new TextMenuOption(player.Localizer["jbstats_lr_top_menu.empty"]));
            return builder.Build();
        }

        var rank = 1;
        foreach (var record in top)
        {
            var name = string.IsNullOrWhiteSpace(record.PlayerName)
                ? record.SteamId.ToString()
                : record.PlayerName;

            builder.AddOption(new TextMenuOption(
                player.Localizer["jbstats_lr_top_menu.row", rank, name, record.LastRequestWins, record.LastRequestLosses]));
            rank++;
        }

        return builder.Build();
    }

    private IMenuAPI LastRequestYourStatsMenu(IJBPlayer player)
    {
        var builder = CreateBuilder(player, "jbstats_lr_your_menu.title");
        var stats = _database.GetPlayerStats(player.SteamID, player.Player.Name);

        builder.AddOption(new TextMenuOption(player.Localizer["jbstats_lr_your_menu.player", player.Player.Name]));
        builder.AddOption(new TextMenuOption(player.Localizer["jbstats_lr_your_menu.wins", stats.LastRequestWins]));
        builder.AddOption(new TextMenuOption(player.Localizer["jbstats_lr_your_menu.losses", stats.LastRequestLosses]));
        builder.AddOption(new TextMenuOption(player.Localizer["jbstats_lr_your_menu.ratio", FormatRatio(stats)]));

        return builder.Build();
    }

    private IMenuAPI SpecialDaysMenu(IJBPlayer player)
    {
        var builder = CreateBuilder(player, "jbstats_sd_menu.title");

        AddSubmenu(builder, player, "jbstats_sd_menu_option.top", () => SpecialDaysTopMenu(player));
        AddSubmenu(builder, player, "jbstats_sd_menu_option.your_stats", () => SpecialDaysYourStatsMenu(player));

        return builder.Build();
    }

    private IMenuAPI SpecialDaysTopMenu(IJBPlayer player)
    {
        var builder = CreateBuilder(player, "jbstats_sd_top_menu.title");
        var top = _database.GetTopSpecialDayPlayers(_config.TopLimit);

        if (top.Count == 0)
        {
            builder.AddOption(new TextMenuOption(player.Localizer["jbstats_sd_top_menu.empty"]));
            return builder.Build();
        }

        var rank = 1;
        foreach (var record in top)
        {
            var name = string.IsNullOrWhiteSpace(record.PlayerName)
                ? record.SteamId.ToString()
                : record.PlayerName;

            builder.AddOption(new TextMenuOption(
                player.Localizer["jbstats_sd_top_menu.row", rank, name, record.SpecialDayWins, record.SpecialDayLosses]));
            rank++;
        }

        return builder.Build();
    }

    private IMenuAPI SpecialDaysYourStatsMenu(IJBPlayer player)
    {
        var builder = CreateBuilder(player, "jbstats_sd_your_menu.title");
        var stats = _database.GetPlayerStats(player.SteamID, player.Player.Name);

        builder.AddOption(new TextMenuOption(player.Localizer["jbstats_sd_your_menu.player", player.Player.Name]));
        builder.AddOption(new TextMenuOption(player.Localizer["jbstats_sd_your_menu.wins", stats.SpecialDayWins]));
        builder.AddOption(new TextMenuOption(player.Localizer["jbstats_sd_your_menu.losses", stats.SpecialDayLosses]));
        builder.AddOption(new TextMenuOption(player.Localizer["jbstats_sd_your_menu.ratio", FormatSpecialDayRatio(stats)]));

        return builder.Build();
    }

    private IMenuBuilderAPI CreateBuilder(IJBPlayer player, string titleKey)
    {
        return _core.MenusAPI.CreateBuilder().Design
            .SetMenuTitle(player.Localizer[titleKey]);
    }

    private static void AddSubmenu(IMenuBuilderAPI builder, IJBPlayer player, string labelKey, Func<IMenuAPI> submenu)
    {
        builder.AddOption(new SubmenuMenuOption(player.Localizer[labelKey], submenu));
    }

    private static string FormatRatio(JBStatsDB.JBStatsRecord stats)
    {
        if (stats.LastRequestLosses <= 0)
            return stats.LastRequestWins.ToString("0.00");

        return ((double)stats.LastRequestWins / stats.LastRequestLosses).ToString("0.00");
    }

    private static string FormatSpecialDayRatio(JBStatsDB.JBStatsRecord stats)
    {
        if (stats.SpecialDayLosses <= 0)
            return stats.SpecialDayWins.ToString("0.00");

        return ((double)stats.SpecialDayWins / stats.SpecialDayLosses).ToString("0.00");
    }
}
