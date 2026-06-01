using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Natives;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.SchemaDefinitions;

namespace Jailbreak;

public sealed class IconManager
{
    public const string CoinModelPath = "models/coop/challenge_coin.vmdl";

    private readonly ISwiftlyCore _core;
    private CHandle<CDynamicProp>? _coinHandle;

    public IconManager(ISwiftlyCore core)
    {
        _core = core;
    }

    public void SpawnCoin(IPlayer warden)
    {
        DespawnCoin();

        if (!warden.IsValid || !warden.IsAlive) return;

        var pawn = warden.PlayerPawn;
        if (pawn == null) return;

        var origin = pawn.AbsOrigin;
        if (origin == null) return;

        var coin = _core.EntitySystem.CreateEntityByDesignerName<CDynamicProp>("prop_dynamic");
        if (coin == null) return;

        var pos = new Vector(origin.Value.X, origin.Value.Y, origin.Value.Z + 90f);

        coin.DispatchSpawn();
        coin.Teleport(pos, null, null);
        coin.SetModel(CoinModelPath);
        coin.AcceptInput("SetAnimation", "challenge_coin_idle");
        coin.AcceptInput("SetParent", "!activator", pawn, coin);
        coin.SetScale(0.65f);

        _coinHandle = _core.EntitySystem.GetRefEHandle(coin);
    }

    public void DespawnCoin()
    {
        if (_coinHandle.HasValue && _coinHandle.Value.IsValid)
        {
            var coin = _coinHandle.Value.Value;
            if (coin != null)
                coin.Despawn();
        }
        _coinHandle = null;
    }

    public void CleanupAll() => DespawnCoin();
}

