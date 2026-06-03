using SwiftlyS2.Shared;

namespace Jailbreak;

/// <summary>
/// Manages cell doors. Injected as a singleton so you can call OpenCells / CloseCells anywhere without passing services.
/// </summary>
public sealed class CellManager(ISwiftlyCore core)
{
    /// <summary>
    /// Whether the cells are open or not. This is used to prevent spamming the open/close inputs when the cells are already in the desired state.
    /// </summary>
    public bool CellsOpen { get; set; } = false;
        
    public void OpenCells()
    {
        CellsOpen = true;

        Utils.ForceEntInput("func_door",          "Open", core.EntitySystem, core.Scheduler);
        Utils.ForceEntInput("func_door_rotating", "Open", core.EntitySystem, core.Scheduler);
        Utils.ForceEntInput("func_movelinear",    "Open", core.EntitySystem, core.Scheduler);
        Utils.ForceEntInput("func_breakable",     "Break", core.EntitySystem, core.Scheduler);
        Utils.ForceEntInput("prop_door_rotating", "Open", core.EntitySystem, core.Scheduler);

    }

    public void CloseCells()
    {
        CellsOpen = false;

        Utils.ForceEntInput("func_door",          "Close", core.EntitySystem, core.Scheduler);
        Utils.ForceEntInput("func_door_rotating", "Close", core.EntitySystem, core.Scheduler);
        Utils.ForceEntInput("func_movelinear",    "Close", core.EntitySystem, core.Scheduler);
        Utils.ForceEntInput("func_breakable",     "Repair", core.EntitySystem, core.Scheduler);     // idk if it works, need to find a way to reset breakables.
        Utils.ForceEntInput("prop_door_rotating", "Close", core.EntitySystem, core.Scheduler);
    }
}
