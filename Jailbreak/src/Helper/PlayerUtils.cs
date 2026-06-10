using Jailbreak.Contract;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Natives;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.Scheduler;
using SwiftlyS2.Shared.SchemaDefinitions;
using SwiftlyS2.Shared.Sounds;

namespace Jailbreak;

public static class PlayerUtils
{
    /// <summary>
    /// Sets the model for a player's pawn on the next world update, ensuring
    /// the call is always on the main game thread.
    /// </summary>
    public static void SetModel(IPlayer player, string model, ISchedulerService scheduler)
    {
        scheduler.NextWorldUpdate(() =>
        {
            if (!player.IsValid) return;
            var pawn = player.PlayerPawn;
            if (pawn == null || !pawn.IsValid) return;
            pawn.SetModel(model);
        });
    }

    /// <summary>
    /// Picks a random model from the list. Returns null if the list is empty.
    /// </summary>
    public static string? PickRandomModel(List<string> models)
    {
        if (models.Count == 0) return null;
        return models[Random.Shared.Next(models.Count)];
    }

    /// <summary>
    /// Sets the render color for a player's pawn on the next world update, ensuring
    /// the call is always on the main game thread. Useful for simple role-based color changes
    /// </summary>
    /// <param name="player"></param>
    /// <param name="color"></param>
    /// <param name="scheduler"></param>
    public static void Color(IPlayer player, Color color, ISchedulerService scheduler)
    {
        var pawn = player.PlayerPawn;
        if (pawn == null || !pawn.IsValid) 
            return;

        scheduler.NextWorldUpdate(() =>
        {
            pawn.RenderMode = RenderMode_t.kRenderTransAlpha;
            pawn.Render = color;
            pawn.RenderUpdated();
            pawn.RenderUpdated();
        });
    }
    /// <summary>
    /// Gives player a certaing weapon
    /// </summary>
    /// <param name="player"></param>
    /// <param name="weapon_name"></param>
    /// <param name="scheduler"></param>
    public static void GiveWeapon(IPlayer player, string weapon_name, ISchedulerService scheduler)
    {
        var pawn = player.Pawn;
        if (pawn == null || !pawn.IsValid)
            return;

        scheduler.NextWorldUpdate(() =>
        {
            pawn.ItemServices?.GiveItem<CBaseEntity>(weapon_name);
        });
    }
    /// <summary>
    /// Freezes player movement by switching movement type to obsolete.
    /// </summary>
    /// <param name="player"></param>
    /// <param name="color">Color player will get while being frozen, default none</param>
    public static void FreezeVelocity(IPlayer player, Color? color = null)
    {
        SetFreezeState(player, MoveType_t.MOVETYPE_OBSOLETE, color);
    }
    /// <summary>
    /// Unfreezes player movement by switching movement type back to walk.
    /// </summary>
    /// <param name="player"></param>
    /// <param name="color">Color player will get while being unfrozen, default none</param>
    public static void UnfreezeVelocity(IPlayer player, Color? color = null)
    {
        SetFreezeState(player, MoveType_t.MOVETYPE_WALK, color);
    }

    private static void SetFreezeState(IPlayer player, MoveType_t moveType, Color? color = null)
    {
        var playerPawn = player.PlayerPawn;
        if (playerPawn == null || !playerPawn.IsValid)
            return;

        if (playerPawn.MoveType != moveType || playerPawn.ActualMoveType != moveType)
        {
            playerPawn.MoveType = moveType;
            playerPawn.ActualMoveType = moveType;
            playerPawn.MoveTypeUpdated();
        }
        
        if (color != null)
        {
            var current = playerPawn.Render;
            if (playerPawn.RenderMode != RenderMode_t.kRenderTransAlpha || !ColorsEqual(current, color.Value))
            {
                playerPawn.RenderMode = RenderMode_t.kRenderTransAlpha;
                playerPawn.Render = color.Value;
                playerPawn.RenderModeUpdated();
                playerPawn.RenderUpdated();
            }
        }
    }

    private static bool ColorsEqual(Color left, Color right)
    {
        return left.R == right.R
            && left.G == right.G
            && left.B == right.B
            && left.A == right.A;
    }

    /// <summary>
    /// Emits sounds to all clients at once.
    /// </summary>
    /// <param name="sound_name">Name of the sound</param>
    /// <param name="sound_volume">Volume the sound should be emitted with</param>
    public static void EmitSoundToAll(this ISwiftlyCore core, string sound_name, float sound_volume)
    {
        var soundEvent = new SoundEvent
        {
            Name = sound_name,
            Volume = sound_volume
        };


        soundEvent.Recipients.AddAllPlayers();
        soundEvent.Emit();
    }

    /// <summary>
    /// Emits sound to a single player.
    /// </summary>
    /// <param name="player"></param>
    /// <param name="sound_name"></param>
    /// <param name="sound_volume"></param>
    public static void EmitSoundToPlayer(this IPlayer player, string sound_name, float sound_volume)
    {
        var soundEvent = new SoundEvent
        {
            Name = sound_name,
            Volume = sound_volume
        };

        soundEvent.Recipients.AddRecipient(player.PlayerID);
        soundEvent.Emit();
    }
}
