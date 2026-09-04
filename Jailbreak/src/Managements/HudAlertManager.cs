using HudText.Contract;
using SwiftlyS2.Shared;

namespace Jailbreak;

public sealed class HudAlertManager
{
    private const int AlertDurationSeconds = 5;

    private readonly ISwiftlyCore _core;
    private IHudTextService? _hudText;

    public HudAlertManager(ISwiftlyCore core)
    {
        _core = core;
    }

    public void SetHudTextService(IHudTextService? hudText)
    {
        _hudText = hudText;
    }

    public void Broadcast(string messageKey, HudAlertSeverity severity, params object[] args)
    {
        if (_hudText is null)
            return;

        foreach (var recipient in _core.PlayerManager.GetAllPlayers())
        {
            if (recipient is not { IsValid: true })
                continue;

            var localizer = _core.Translation.GetPlayerLocalizer(recipient);
            var message = args.Length == 0 ? localizer[messageKey] : localizer[messageKey, args];

            _hudText.ShowAlert(recipient, localizer["hud_alert_title"], message, new HudAlertOptions
            {
                Severity = severity,
                Duration = AlertDurationSeconds,
            });
        }
    }
}
