using SwiftlyS2.Shared.EntitySystem;
using SwiftlyS2.Shared.Scheduler;
using SwiftlyS2.Shared.SchemaDefinitions;

namespace Jailbreak;

/// <summary>
/// Utility class for various helper methods and common functionality used across the plugin.
/// </summary>
public static class Utils
{
    /// <summary>
    /// Fires an input on all entities matching the given designer name on the next world update.
    /// Safe to call from any context. Useful for triggering map entities like doors or buttons.
    /// </summary>
    /// <example>
    /// Utils.ForceEntInput("func_door", "Open", entService, scheduler);
    /// Utils.ForceEntInput("func_door_rotating", "Close", entService, scheduler);
    /// </example>
    public static void ForceEntInput(string designerName, string input, IEntitySystemService entService, ISchedulerService scheduler)
    {
        scheduler.NextWorldUpdate(() =>
        {
            foreach (CBaseEntity ent in entService.GetAllEntitiesByDesignerName<CBaseEntity>(designerName))
            {
                if (!ent.IsValid) continue;
                ent.AcceptInput<string>(input, null);
            }
        });
    }
}