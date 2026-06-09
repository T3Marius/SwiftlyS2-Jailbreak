using Jailbreak.Contract;
using Microsoft.Extensions.Options;
using SwiftlyS2.Core.Menus.OptionsBase;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Helpers;
using SwiftlyS2.Shared.Menus;
using SwiftlyS2.Shared.Players;

namespace Jailbreak;

public sealed class PrisonerCommands
{
    private readonly ISwiftlyCore _core;
    private readonly IJBPlayerManagement _players;
    private readonly LastRequestManager _lastRequestManager;
    private readonly SpecialDayManager _specialDayManager;
    private readonly PrisonerConfig _config;
    private readonly HashSet<ulong> _pendingSurrenders = [];

    public PrisonerCommands(
        ISwiftlyCore core,
        IJBPlayerManagement players,
        LastRequestManager lastRequestManager,
        SpecialDayManager specialDayManager,
        IOptions<PrisonerConfig> config)
    {
        _core = core;
        _players = players;
        _lastRequestManager = lastRequestManager;
        _specialDayManager = specialDayManager;
        _config = config.Value;
    }

    public void Register()
    {
        foreach (var command in _config.Commands.LastRequest)
        {
            if (!_core.Command.IsCommandRegistered(command))
                _core.Command.RegisterCommand(command, LastRequestCommand);
        }

        foreach (var command in _config.Commands.Surrender)
        {
            if (!_core.Command.IsCommandRegistered(command))
                _core.Command.RegisterCommand(command, SurrenderCommand);
        }

    }

    public void Unregister()
    {
        foreach (var command in _config.Commands.LastRequest)
        {
            if (_core.Command.IsCommandRegistered(command))
                _core.Command.UnregisterCommand(command);
        }

        foreach (var command in _config.Commands.Surrender)
        {
            if (_core.Command.IsCommandRegistered(command))
                _core.Command.UnregisterCommand(command);
        }

        _pendingSurrenders.Clear();
    }

    private void LastRequestCommand(ICommandContext ctx)
    {
        if (ctx.Sender == null)
            return;

        var player = _players.SyncPlayer(ctx.Sender);
        if (player == null)
            return;

        if (!ValidateCanOpenLastRequest(player))
            return;

        _core.MenusAPI.OpenMenuForPlayer(player.Player, LastRequestMenu(player));
    }

    private void SurrenderCommand(ICommandContext ctx)
    {
        if (ctx.Sender == null)
            return;

        var prisoner = _players.SyncPlayer(ctx.Sender);
        if (prisoner == null)
            return;

        if (!ValidateCanSurrender(prisoner))
            return;

        var warden = _players.GetWarden();
        if (warden == null || !warden.Player.IsValid)
        {
            prisoner.SendMessage(MessageType.Chat, "surrender_no_warden", true);
            return;
        }

        var prisonerKey = PlayerIdentity.GetKey(prisoner.Player);
        if (!_pendingSurrenders.Add(prisonerKey))
        {
            prisoner.SendMessage(MessageType.Chat, "surrender_already_pending", true);
            return;
        }

        prisoner.SendMessage(MessageType.Chat, "surrender_request_sent", true, args: warden.Player.Name);
        warden.SendMessage(MessageType.Chat, "surrender_request_received", true, args: prisoner.Player.Name);
        _core.MenusAPI.OpenMenuForPlayer(warden.Player, SurrenderMenu(warden, prisoner, prisonerKey));
    }

    private IMenuAPI LastRequestMenu(IJBPlayer prisoner)
    {
        var builder = CreateBuilder(prisoner, "last_request_menu.title");
        var lastRequests = _lastRequestManager.LastRequests.OrderBy(lr => lr.Name).ToList();

        if (lastRequests.Count == 0)
        {
            builder.AddOption(new TextMenuOption(prisoner.Localizer["last_request_menu.no_requests"]));
            return builder.Build();
        }

        foreach (var lastRequest in lastRequests)
        {
            AddButton(builder, lastRequest.Name, () =>
            {
                _core.Scheduler.NextWorldUpdate(() =>
                {
                    if (!ValidateCanOpenLastRequest(prisoner))
                        return;

                    OpenWeaponStepOrNext(prisoner, lastRequest);
                });
            });
        }

        return builder.Build();
    }

    private IMenuAPI SurrenderMenu(IJBPlayer warden, IJBPlayer prisoner, ulong prisonerKey)
    {
        var builder = _core.MenusAPI.CreateBuilder().Design
            .SetMenuTitle(warden.Localizer["surrender_menu.title", prisoner.Player.Name]);

        AddButton(builder, warden.Localizer["surrender_menu_option.accept"], () =>
        {
            _core.Scheduler.NextWorldUpdate(() =>
            {
                if (!ValidatePendingSurrender(warden, prisoner, prisonerKey))
                    return;

                _pendingSurrenders.Remove(prisonerKey);
                prisoner.SetRebel(false);
                StripWeapons(prisoner.Player);
                _players.SendMessage(MessageType.Chat, "surrender_accepted", true, args: [warden.Player.Name, prisoner.Player.Name]);
                _core.MenusAPI.CloseActiveMenu(warden.Player);
            });
        });

        AddButton(builder, warden.Localizer["surrender_menu_option.refuse"], () =>
        {
            _core.Scheduler.NextWorldUpdate(() =>
            {
                if (_pendingSurrenders.Remove(prisonerKey))
                {
                    _players.SendMessage(MessageType.Chat, "surrender_refused", true, args: [warden.Player.Name, prisoner.Player.Name]);
                }

                if (warden.Player.IsValid)
                    _core.MenusAPI.CloseActiveMenu(warden.Player);
            });
        });

        return builder.Build();
    }

    private void OpenWeaponStepOrNext(IJBPlayer prisoner, ILastRequest lastRequest)
    {
        if (lastRequest.WeaponSelection == LastRequestWeaponSelection.None)
        {
            OpenVariantStepOrNext(prisoner, lastRequest, null);
            return;
        }

        var weapons = GetMenuWeapons(lastRequest).ToList();
        if (weapons.Count == 0)
        {
            if (lastRequest.WeaponSelection == LastRequestWeaponSelection.Required)
            {
                prisoner.SendMessage(MessageType.Chat, "last_request_no_weapons", true);
                return;
            }

            OpenVariantStepOrNext(prisoner, lastRequest, null);
            return;
        }

        _core.MenusAPI.OpenMenuForPlayer(prisoner.Player, WeaponMenu(prisoner, lastRequest, weapons));
    }

    private IMenuAPI WeaponMenu(IJBPlayer prisoner, ILastRequest lastRequest, IReadOnlyList<ItemDefinitionIndex> weapons)
    {
        var builder = CreateBuilder(prisoner, "last_request_weapon_menu.title");

        if (lastRequest.WeaponSelection == LastRequestWeaponSelection.Optional)
        {
            AddButton(builder, prisoner.Localizer["last_request_weapon_menu.no_weapon"], () =>
            {
                _core.Scheduler.NextWorldUpdate(() => OpenVariantStepOrNext(prisoner, lastRequest, null));
            });
        }

        foreach (var weapon in weapons)
        {
            AddButton(builder, GetWeaponLabel(weapon), () =>
            {
                _core.Scheduler.NextWorldUpdate(() => OpenVariantStepOrNext(prisoner, lastRequest, weapon));
            });
        }

        return builder.Build();
    }

    private void OpenVariantStepOrNext(IJBPlayer prisoner, ILastRequest lastRequest, ItemDefinitionIndex? weapon)
    {
        if (lastRequest.Variants.Count == 0)
        {
            OpenGuardStepOrStart(prisoner, lastRequest, weapon, null);
            return;
        }

        _core.MenusAPI.OpenMenuForPlayer(prisoner.Player, VariantMenu(prisoner, lastRequest, weapon));
    }

    private IMenuAPI VariantMenu(IJBPlayer prisoner, ILastRequest lastRequest, ItemDefinitionIndex? weapon)
    {
        var builder = CreateBuilder(prisoner, "last_request_variant_menu.title");

        foreach (var variant in lastRequest.Variants)
        {
            AddButton(builder, variant.Name, () =>
            {
                _core.Scheduler.NextWorldUpdate(() => OpenGuardStepOrStart(prisoner, lastRequest, weapon, variant));
            });
        }

        return builder.Build();
    }

    private void OpenGuardStepOrStart(IJBPlayer prisoner, ILastRequest lastRequest, ItemDefinitionIndex? weapon, LastRequestVariant? variant)
    {
        if (!lastRequest.RequiresGuardSelection)
        {
            StartLastRequest(prisoner, lastRequest, weapon, variant, null);
            return;
        }

        var guards = _players.GetPlayersByTeam(JBTeam.Guard)
            .Where(IsAlive)
            .OrderBy(guard => guard.Player.Name)
            .ToList();

        if (guards.Count == 0)
        {
            prisoner.SendMessage(MessageType.Chat, "last_request_no_guards", true);
            return;
        }

        _core.MenusAPI.OpenMenuForPlayer(prisoner.Player, GuardMenu(prisoner, lastRequest, weapon, variant, guards));
    }

    private IMenuAPI GuardMenu(
        IJBPlayer prisoner,
        ILastRequest lastRequest,
        ItemDefinitionIndex? weapon,
        LastRequestVariant? variant,
        IReadOnlyList<IJBPlayer> guards)
    {
        var builder = CreateBuilder(prisoner, "last_request_guard_menu.title");

        foreach (var guard in guards)
        {
            var guardRef = guard;
            AddButton(builder, guardRef.Player.Name, () =>
            {
                _core.Scheduler.NextWorldUpdate(() => StartLastRequest(prisoner, lastRequest, weapon, variant, guardRef));
            });
        }

        return builder.Build();
    }

    private void StartLastRequest(
        IJBPlayer prisoner,
        ILastRequest lastRequest,
        ItemDefinitionIndex? weapon,
        LastRequestVariant? variant,
        IJBPlayer? guard)
    {
        if (!ValidateCanOpenLastRequest(prisoner))
            return;

        if (guard != null && !IsAlive(guard))
        {
            prisoner.SendMessage(MessageType.Chat, "last_request_guard_unavailable", true);
            return;
        }

        var context = new LastRequestStartContext(prisoner, guard, weapon, variant);
        if (!_lastRequestManager.StartLastRequest(lastRequest, context))
        {
            prisoner.SendMessage(MessageType.Chat, "last_request_cannot_start", true, args: lastRequest.Name);
            return;
        }

        _core.MenusAPI.CloseActiveMenu(prisoner.Player);
    }

    private bool ValidateCanOpenLastRequest(IJBPlayer prisoner)
    {
        if (_specialDayManager.IsSpecialDayActive)
        {
            prisoner.SendMessage(MessageType.Chat, "special_day_active_blocked", true);
            return false;
        }

        if (_lastRequestManager.CurrentLastRequest != null)
        {
            prisoner.SendMessage(MessageType.Chat, "last_request_already_active", true);
            return false;
        }

        if (prisoner.Team != JBTeam.Prisoner)
        {
            prisoner.SendMessage(MessageType.Chat, "last_request_only_prisoner", true);
            return false;
        }

        if (!prisoner.Player.IsValid || !prisoner.Player.IsAlive)
        {
            prisoner.SendMessage(MessageType.Chat, "last_request_must_be_alive", true);
            return false;
        }

        if (prisoner.IsRebel)
        {
            prisoner.SendMessage(MessageType.Chat, "last_request_rebel_blocked", true);
            return false;
        }

        if (_lastRequestManager.GetEligibleLastRequestPrisoners().Count != 1)
        {
            prisoner.SendMessage(MessageType.Chat, "last_request_not_last_prisoner", true);
            return false;
        }

        return true;
    }

    private bool ValidateCanSurrender(IJBPlayer prisoner)
    {
        if (_specialDayManager.IsSpecialDayActive)
        {
            prisoner.SendMessage(MessageType.Chat, "special_day_active_blocked", true);
            return false;
        }

        if (_lastRequestManager.IsLastRequestActive)
        {
            prisoner.SendMessage(MessageType.Chat, "last_request_already_active", true);
            return false;
        }

        if (prisoner.Team != JBTeam.Prisoner)
        {
            prisoner.SendMessage(MessageType.Chat, "surrender_only_prisoner", true);
            return false;
        }

        if (!prisoner.Player.IsValid || !prisoner.Player.IsAlive)
        {
            prisoner.SendMessage(MessageType.Chat, "surrender_must_be_alive", true);
            return false;
        }

        if (!prisoner.IsRebel)
        {
            prisoner.SendMessage(MessageType.Chat, "surrender_not_rebel", true);
            return false;
        }

        return true;
    }

    private bool ValidatePendingSurrender(IJBPlayer warden, IJBPlayer prisoner, ulong prisonerKey)
    {
        if (!_pendingSurrenders.Contains(prisonerKey))
            return false;

        if (!warden.Player.IsValid || !warden.IsWarden)
        {
            _pendingSurrenders.Remove(prisonerKey);
            return false;
        }

        if (!prisoner.Player.IsValid || !prisoner.Player.IsAlive || !prisoner.IsRebel)
        {
            _pendingSurrenders.Remove(prisonerKey);
            warden.SendMessage(MessageType.Chat, "surrender_unavailable", true);
            return false;
        }

        return true;
    }

    private IEnumerable<ItemDefinitionIndex> GetMenuWeapons(ILastRequest lastRequest)
    {
        var weapons = lastRequest.WeaponMenuWeapons.Count > 0
            ? lastRequest.WeaponMenuWeapons
            : lastRequest.AllowAllWeapons
                ? LastRequestWeapons.GunsMenuWeapons.ToArray()
                : [];

        return weapons.Where(weapon => lastRequest.AllowAllWeapons || lastRequest.AllowedWeapons.Contains(weapon));
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

    private IMenuBuilderAPI CreateBuilder(IJBPlayer player, string titleKey)
    {
        return _core.MenusAPI.CreateBuilder().Design
            .SetMenuTitle(player.Localizer[titleKey]);
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

    private static bool IsAlive(IJBPlayer player)
    {
        return player.Player.IsValid && player.Player.IsAlive;
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
}
