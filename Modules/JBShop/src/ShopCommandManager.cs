using System.Globalization;
using Jailbreak.Contract;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.Translation;

namespace JBShop;

internal sealed class ShopCommandManager
{
    private readonly ISwiftlyCore _core;
    private readonly ShopCommandsConfig _config;
    private readonly HashSet<string> _registeredCommands = new(StringComparer.OrdinalIgnoreCase);
    private IJailbreak? _jailbreak;

    public ShopCommandManager(ISwiftlyCore core, ShopCommandsConfig config)
    {
        _core = core;
        _config = config;
    }

    public void Register(IJailbreak jailbreak, Action<IJBPlayer> openShop)
    {
        _jailbreak = jailbreak;
        RegisterAliases(_config.ShopCommands, context =>
        {
            var player = GetSender(context);
            if (player != null)
                openShop(player);
        });
        RegisterAliases(_config.BalanceCommands, BalanceCommand);
        RegisterAliases(_config.GiftCommands, GiftCommand);
        RegisterAliases(_config.AddBalanceCommands, context => AdminBalanceCommand(context, AdminBalanceOperation.Add));
        RegisterAliases(_config.SubtractBalanceCommands, context => AdminBalanceCommand(context, AdminBalanceOperation.Subtract));
        RegisterAliases(_config.SetBalanceCommands, context => AdminBalanceCommand(context, AdminBalanceOperation.Set));
    }

    public void Unregister()
    {
        foreach (var command in _registeredCommands)
        {
            if (_core.Command.IsCommandRegistered(command))
                _core.Command.UnregisterCommand(command);
        }

        _registeredCommands.Clear();
        _jailbreak = null;
    }

    private void BalanceCommand(ICommandContext context)
    {
        var player = GetSender(context);
        if (player == null || _jailbreak == null)
            return;

        var localizer = GetLocalizer(player);
        if (!_jailbreak.Shop.IsEconomyAvailable)
        {
            Reply(context, localizer, "shop.economy_unavailable");
            return;
        }

        var currencies = _jailbreak.Shop.Currencies;
        Reply(context, localizer, "shop.balance_header");

        if (currencies.Count == 0)
        {
            Reply(context, localizer, "shop.balance_empty");
            return;
        }

        foreach (var currency in currencies)
        {
            var balance = _jailbreak.Shop.GetBalance(player, currency);
            context.Reply(localizer["shop.balance_item", FormatCurrency(currency), FormatAmount(balance)]);
        }
    }

    private void GiftCommand(ICommandContext context)
    {
        var sender = GetSender(context);
        if (sender == null || _jailbreak == null)
            return;

        var localizer = GetLocalizer(sender);
        if (context.Args.Length < 3)
        {
            Reply(context, localizer, "shop.gift_usage", context.CommandName);
            return;
        }

        var recipient = FindTarget(sender.Player, context.Args[0]);
        if (recipient == null)
        {
            Reply(context, localizer, "shop.target_invalid");
            return;
        }

        if (!TryParsePositiveAmount(context.Args[1], out var amount))
        {
            Reply(context, localizer, "shop.amount_invalid");
            return;
        }

        var currency = context.Args[2];
        var result = _jailbreak.Shop.TransferBalance(sender, recipient, currency, amount);
        if (!result.Success)
        {
            ReplyBalanceError(context, localizer, result.Status, currency);
            return;
        }

        Reply(context, localizer, "shop.gift_sent", FormatAmount(amount), FormatCurrency(currency), recipient.Player.Name);

        var recipientLocalizer = GetLocalizer(recipient);
        recipient.Player.SendChat($"{recipientLocalizer["shop.prefix"]}{recipientLocalizer["shop.gift_received", FormatAmount(amount), FormatCurrency(currency), sender.Player.Name]}");
    }

    private void AdminBalanceCommand(ICommandContext context, AdminBalanceOperation operation)
    {
        var sender = GetSender(context);
        if (sender == null || _jailbreak == null)
            return;

        var localizer = GetLocalizer(sender);
        if (_config.AdminPermissions.Count > 0
            && !_core.Permission.PlayerHasPermissions(sender.SteamID, _config.AdminPermissions))
        {
            Reply(context, localizer, "shop.no_permission");
            return;
        }

        if (context.Args.Length < 3)
        {
            Reply(context, localizer, "shop.admin_usage", context.CommandName);
            return;
        }

        var target = FindTarget(sender.Player, context.Args[0]);
        if (target == null)
        {
            Reply(context, localizer, "shop.target_invalid");
            return;
        }

        var amountIsValid = operation == AdminBalanceOperation.Set
            ? decimal.TryParse(context.Args[1], NumberStyles.Number, CultureInfo.InvariantCulture, out var amount) && amount >= 0
            : TryParsePositiveAmount(context.Args[1], out amount);
        if (!amountIsValid)
        {
            Reply(context, localizer, "shop.amount_invalid");
            return;
        }

        var currency = context.Args[2];
        var result = operation switch
        {
            AdminBalanceOperation.Add => _jailbreak.Shop.AddBalance(target, currency, amount),
            AdminBalanceOperation.Subtract => _jailbreak.Shop.SubtractBalance(target, currency, amount),
            _ => _jailbreak.Shop.SetBalance(target, currency, amount)
        };

        if (!result.Success)
        {
            ReplyBalanceError(context, localizer, result.Status, currency);
            return;
        }

        Reply(context, localizer, "shop.admin_success",
            operation.ToString(), target.Player.Name, FormatCurrency(currency), FormatAmount(result.Balance));

        var targetLocalizer = GetLocalizer(target);
        target.Player.SendChat($"{targetLocalizer["shop.prefix"]}{targetLocalizer["shop.admin_target_updated", FormatCurrency(currency), FormatAmount(result.Balance)]}");
    }

    private void RegisterAliases(IEnumerable<string> aliases, ICommandService.CommandListener handler)
    {
        foreach (var command in aliases.Where(command => !string.IsNullOrWhiteSpace(command)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (_core.Command.IsCommandRegistered(command))
                continue;

            _core.Command.RegisterCommand(command, handler);
            _registeredCommands.Add(command);
        }
    }

    private IJBPlayer? GetSender(ICommandContext context)
    {
        if (context.Sender is not { IsValid: true } sender || sender.IsFakeClient || _jailbreak == null)
            return null;

        return _jailbreak.Players.SyncPlayer(sender);
    }

    private IJBPlayer? FindTarget(IPlayer sender, string query)
    {
        if (_jailbreak == null)
            return null;

        var target = _core.PlayerManager.FindTargettedPlayers(
                sender,
                query,
                TargetSearchMode.NoMultipleTargets | TargetSearchMode.NoBots | TargetSearchMode.IncludeSelf)
            .FirstOrDefault();

        return target == null ? null : _jailbreak.Players.SyncPlayer(target);
    }

    private ILocalizer GetLocalizer(IJBPlayer player) =>
        _core.Translation.GetPlayerLocalizer(player.Player);

    private static bool TryParsePositiveAmount(string value, out decimal amount) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out amount) && amount > 0;

    private static string FormatCurrency(string currency) =>
        CultureInfo.InvariantCulture.TextInfo.ToTitleCase(currency.ToLowerInvariant());

    private static string FormatAmount(decimal amount) =>
        amount.ToString("0.##", CultureInfo.InvariantCulture);

    private static void Reply(ICommandContext context, ILocalizer localizer, string key, params object[] args)
    {
        var message = args.Length == 0 ? localizer[key] : localizer[key, args];
        context.Reply($"{localizer["shop.prefix"]}{message}");
    }

    private static void ReplyBalanceError(
        ICommandContext context,
        ILocalizer localizer,
        ShopBalanceStatus status,
        string currency)
    {
        var key = status switch
        {
            ShopBalanceStatus.EconomyUnavailable => "shop.economy_unavailable",
            ShopBalanceStatus.InvalidCurrency => "shop.currency_invalid",
            ShopBalanceStatus.InvalidAmount => "shop.amount_invalid",
            ShopBalanceStatus.InsufficientFunds => "shop.gift_no_funds",
            ShopBalanceStatus.SamePlayer => "shop.gift_self",
            ShopBalanceStatus.InvalidPlayer => "shop.target_invalid",
            _ => "shop.balance_action_failed"
        };

        Reply(context, localizer, key, FormatCurrency(currency));
    }

    private enum AdminBalanceOperation
    {
        Add,
        Subtract,
        Set
    }
}
