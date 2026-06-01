using Jailbreak.Contract;
using Microsoft.Extensions.Options;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Events;
using SwiftlyS2.Shared.Players;

namespace Jailbreak;

public sealed class Listeners
{
    private readonly ISwiftlyCore          _core;
    private readonly ModelsConfig          _modelsConfig;

    public Listeners(ISwiftlyCore core, IJBPlayerManagement playerManagement, IOptions<ModelsConfig> modelsConfig)
    {
        _core    = core;
        _modelsConfig = modelsConfig.Value;
    }

    public void Register()
    {
        _core.Event.OnPrecacheResource += OnPrecacheResource;
        _core.Event.OnMapUnload        += OnMapUnload;
    }

    public void Unregister()
    {
        _core.Event.OnPrecacheResource -= OnPrecacheResource;
        _core.Event.OnMapUnload        -= OnMapUnload;
    }

    private void OnPrecacheResource(IOnPrecacheResourceEvent @event)
    {
        @event.AddItem(_modelsConfig.WardenModel);
        @event.AddItem(_modelsConfig.DeputyModel);
        @event.AddItem(_modelsConfig.FreedayModel);
        foreach (var m in _modelsConfig.GuardModels)    @event.AddItem(m);
        foreach (var m in _modelsConfig.PrisonerModels) @event.AddItem(m);
    }

    private void OnMapUnload(IOnMapUnloadEvent @event)
    {
    }
}
