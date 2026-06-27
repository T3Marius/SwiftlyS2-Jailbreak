namespace Jailbreak.Contract;

public interface ICellsManager
{
    bool CellsOpen { get; set; }
    void OpenCells();
    void CloseCells();
}