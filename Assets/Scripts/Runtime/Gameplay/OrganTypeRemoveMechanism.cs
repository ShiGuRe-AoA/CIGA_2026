using UnityEngine;

/// <summary>
/// Mechanism that removes one organ type from play when the matching organ is on its cell.
/// </summary>
public class OrganTypeRemoveMechanism : Mechanism
{
    [Header("Target Type")]
    [SerializeField] private OrganType targetOrganType = OrganType.Hand;

    [Header("Debug")]
    [SerializeField] private bool showDebugLog = true;

    private Vector3Int gridPos;

    private MapGrid Grid => GameBootstrap.Instance?.MapGrid;
    private OrganController Controller => GameBootstrap.Instance?.OrganController;

    private void Start()
    {
        SnapToGrid();
    }

    public override void OnTriggered(Trigger source)
    {
        base.OnTriggered(source);

        if (targetOrganType == OrganType.Heart)
        {
            Log("Heart cannot be removed.");
            return;
        }

        if (Grid == null || Controller == null)
            return;

        SnapToGrid();

        OrganUnit organAtCell = Controller.GetOrganAt(gridPos);
        if (organAtCell == null ||
            organAtCell.RemovedAsOrgan ||
            organAtCell.OrganType != targetOrganType)
        {
            return;
        }

        AudioPlayer.PlayOneShot("SFX_OrganLost");

        Grid.SetHeldOrganCount(targetOrganType, 0);
        Controller.RemoveOrganFromGameplay(organAtCell);
        ReplaceRemainingOrgans();

        Log($"Removed held count for {targetOrganType}.");
    }

    private void ReplaceRemainingOrgans()
    {
        foreach (OrganUnit organ in Controller.Organs)
        {
            if (organ == null ||
                organ.RemovedAsOrgan ||
                organ.OrganType != targetOrganType)
            {
                continue;
            }

            OrganType replacementType = FindReplacementTypeFor(organ);
            if (replacementType == OrganType.Heart)
            {
                Log($"No valid replacement type found for {organ.name}.");
                continue;
            }

            Controller.ForceSwitchOrganType(organ, replacementType);
        }
    }

    private OrganType FindReplacementTypeFor(OrganUnit organ)
    {
        if (CanUseReplacement(organ, OrganType.Foot))
            return OrganType.Foot;

        if (CanUseReplacement(organ, OrganType.Hand))
            return OrganType.Hand;

        if (CanUseReplacement(organ, OrganType.Eye))
            return OrganType.Eye;

        return OrganType.Heart;
    }

    private bool CanUseReplacement(OrganUnit organ, OrganType type)
    {
        return type != targetOrganType &&
               type != OrganType.Heart &&
               Controller != null &&
               Controller.CanSwitchOrganType(organ, type);
    }

    private void SnapToGrid()
    {
        if (Grid == null)
            return;

        gridPos = Grid.WorldToCell(transform.position);
        transform.position = Grid.CellToWorld(gridPos);
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = targetOrganType switch
        {
            OrganType.Foot => Color.blue,
            OrganType.Hand => Color.green,
            OrganType.Eye => Color.yellow,
            _ => Color.gray
        };

        Gizmos.DrawWireCube(transform.position, Vector3.one * 0.85f);
    }

    private void Log(string message)
    {
        if (!showDebugLog)
            return;

        Debug.Log($"[OrganTypeRemoveMechanism] {name}: {message}", this);
    }
}
