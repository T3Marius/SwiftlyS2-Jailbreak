using Jailbreak.Contract;
using SwiftlyS2.Shared.Natives;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.Scheduler;
using SwiftlyS2.Shared.SchemaDefinitions;

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

    public static void FreezeVelocity(IPlayer player, Color? color = null)
    {
        var playerPawn = player.PlayerPawn;
        if (playerPawn == null)
            return;

        playerPawn.VelocityModifier = 0;
        playerPawn.VelocityModifierUpdated();

        if (color != null)
        {
            playerPawn.RenderMode = RenderMode_t.kRenderTransAlpha;
            playerPawn.Render = color.Value;
            playerPawn.RenderModeUpdated();
            playerPawn.RenderUpdated();
        }
    }
    public static void UnfreezeVelocity(IPlayer player, Color? color = null)
    {
        var playerPawn = player.PlayerPawn;
        if (playerPawn == null)
            return;

        playerPawn.VelocityModifier = 1;
        playerPawn.VelocityModifierUpdated();

        if (color != null)
        {
            playerPawn.RenderMode = RenderMode_t.kRenderTransAlpha;
            playerPawn.Render = color.Value;
            playerPawn.RenderModeUpdated();
            playerPawn.RenderUpdated();
        }
    }
}
