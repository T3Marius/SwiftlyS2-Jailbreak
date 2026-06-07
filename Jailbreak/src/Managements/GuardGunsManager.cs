using Jailbreak.Contract;
using Microsoft.Extensions.Options;
using SwiftlyS2.Core.Menus.OptionsBase;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Helpers;
using SwiftlyS2.Shared.Menus;
using SwiftlyS2.Shared.Natives;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.SchemaDefinitions;

namespace Jailbreak;

public sealed class GuardGunsManager
{
    private readonly ISwiftlyCore _core;
    private readonly IJBPlayerManagement _players;
    private readonly GuardGunsDatabase _database;
    private readonly SpecialDayManager _specialDayManager;
    private readonly WardenConfig _config;

    public GuardGunsManager(
        ISwiftlyCore core,
        IJBPlayerManagement players,
        GuardGunsDatabase database,
        SpecialDayManager specialDayManager,
        IOptions<WardenConfig> config)
    {
        _core = core;
        _players = players;
        _database = database;
        _specialDayManager = specialDayManager;
        _config = config.Value;
    }

    public void Register()
    {
        foreach (var command in _config.Commands.GuardGuns)
        {
            if (!_core.Command.IsCommandRegistered(command))
                _core.Command.RegisterCommand(command, GunsCommand);
        }
    }

    public void Unregister()
    {
        foreach (var command in _config.Commands.GuardGuns)
        {
            if (_core.Command.IsCommandRegistered(command))
                _core.Command.UnregisterCommand(command);
        }
    }

    public void GiveSavedLoadout(IJBPlayer player)
    {
        if (player.Team != JBTeam.Guard || _specialDayManager.IsSpecialDayActive)
            return;

        var settings = _database.GetSettings(player.SteamID);
        if (settings == null)
            return;

        _core.Scheduler.NextWorldUpdate(() => GiveGuardLoadout(player.Player, settings.PrimaryWeapon, settings.SecondaryWeapon));
    }

    private void GunsCommand(ICommandContext ctx)
    {
        if (ctx.Sender == null)
            return;

        var player = _players.SyncPlayer(ctx.Sender);
        if (player == null)
            return;

        if (!CanUseGunsMenu(player))
            return;

        _core.MenusAPI.OpenMenuForPlayer(player.Player, PrimaryGunsMenu(player));
    }

    private IMenuAPI PrimaryGunsMenu(IJBPlayer player)
    {
        var builder = CreateBuilder(player, "guard_guns_primary_menu.title");

        foreach (var weapon in SpecialDayWeapons.PrimaryWeapons.OrderBy(GetWeaponLabel))
        {
            AddButton(builder, GetWeaponLabel(weapon), () =>
            {
                _core.Scheduler.NextWorldUpdate(() =>
                {
                    if (CanUseGunsMenu(player))
                        _core.MenusAPI.OpenMenuForPlayer(player.Player, SecondaryGunsMenu(player, weapon));
                });
            });
        }

        return builder.Build();
    }

    private IMenuAPI SecondaryGunsMenu(IJBPlayer player, ItemDefinitionIndex primaryWeapon)
    {
        var builder = CreateBuilder(player, "guard_guns_secondary_menu.title");

        foreach (var weapon in SpecialDayWeapons.SecondaryWeapons.OrderBy(GetWeaponLabel))
        {
            AddButton(builder, GetWeaponLabel(weapon), () =>
            {
                _core.Scheduler.NextWorldUpdate(() =>
                {
                    if (!CanUseGunsMenu(player))
                        return;

                    _database.SaveSettings(player.SteamID, primaryWeapon, weapon);
                    GiveGuardLoadout(player.Player, primaryWeapon, weapon);
                    player.SendMessage(
                        MessageType.Chat,
                        "guard_guns_selected",
                        true,
                        args: [GetWeaponLabel(primaryWeapon), GetWeaponLabel(weapon)]);
                    _core.MenusAPI.CloseActiveMenu(player.Player);
                });
            });
        }

        return builder.Build();
    }

    private bool CanUseGunsMenu(IJBPlayer player)
    {
        if (_specialDayManager.IsSpecialDayActive)
        {
            player.SendMessage(MessageType.Chat, "special_day_active_blocked", true);
            return false;
        }

        if (player.Team != JBTeam.Guard)
        {
            player.SendMessage(MessageType.Chat, "guard_guns_only_guard", true);
            return false;
        }

        if (!player.Player.IsValid || !player.Player.IsAlive)
        {
            player.SendMessage(MessageType.Chat, "guard_guns_must_be_alive", true);
            return false;
        }

        return true;
    }

    private void GiveGuardLoadout(IPlayer player, ItemDefinitionIndex primaryWeapon, ItemDefinitionIndex secondaryWeapon)
    {
        if (!player.IsValid || !player.IsAlive)
            return;

        StripWeapons(player);

        GiveWeaponByDefinition(player, primaryWeapon);
        GiveWeaponByDefinition(player, secondaryWeapon);
        GiveWeapon(player, "weapon_knife");
    }

    private void GiveWeaponByDefinition(IPlayer player, ItemDefinitionIndex weapon)
    {
        var classname = _core.Helpers.GetClassnameByDefinitionIndex(weapon);
        if (!string.IsNullOrEmpty(classname))
            GiveWeapon(player, classname);
    }

    private static void StripWeapons(IPlayer player)
    {
        var weaponServices = player.PlayerPawn?.WeaponServices;
        if (weaponServices == null)
            return;

        foreach (var weapon in weaponServices.MyValidWeapons.ToList())
        {
            if (weapon?.IsValid == true)
                weaponServices.RemoveWeapon(weapon);
        }
    }

    private static void GiveWeapon(IPlayer player, string weaponName)
    {
        var pawn = player.Pawn;
        if (pawn == null || !pawn.IsValid)
            return;

        pawn.ItemServices?.GiveItem<CBaseEntity>(weaponName);
    }

    private IMenuBuilderAPI CreateBuilder(IJBPlayer player, string titleKey)
    {
        return _core.MenusAPI.CreateBuilder().Design
            .SetMenuTitle(player.Localizer[titleKey]);
    }

    private string GetWeaponLabel(ItemDefinitionIndex weapon)
    {
        var classname = _core.Helpers.GetClassnameByDefinitionIndex(weapon);
        if (string.IsNullOrEmpty(classname))
            return weapon.ToString();

        return classname
            .Replace("weapon_", "", StringComparison.OrdinalIgnoreCase)
            .Replace('_', ' ')
            .ToUpperInvariant();
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
}
