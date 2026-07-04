using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Tilemaps;

/// <summary>
/// 编辑器 Tilemap 单元格可视化。
/// 选中 Tilemap 时，在 Scene 视图中清晰显示 Tilemap 的格子边界和已有 Tile 的格子。
/// </summary>
public static class TilemapEditorGizmos
{
    private const int MaxFilledCellDrawCount = 3000;

    private static readonly Color GridLineColor =
        new Color(0.15f, 0.75f, 1f, 0.85f);

    private static readonly Color BoundsColor =
        new Color(1f, 0.9f, 0.15f, 0.95f);

    private static readonly Color FilledCellColor =
        new Color(0.2f, 0.9f, 1f, 0.16f);

    private static readonly Color FilledCellOutlineColor =
        new Color(0.2f, 0.9f, 1f, 0.45f);

    [DrawGizmo(GizmoType.Selected | GizmoType.Active)]
    private static void DrawSelectedTilemapGrid(
        Tilemap tilemap,
        GizmoType gizmoType)
    {
        if (tilemap == null || tilemap.layoutGrid == null)
            return;

        BoundsInt bounds = tilemap.cellBounds;
        if (bounds.size.x <= 0 || bounds.size.y <= 0)
            return;

        CompareFunction oldZTest = Handles.zTest;
        Handles.zTest = CompareFunction.Always;

        DrawFilledCells(tilemap, bounds);
        DrawGridLines(tilemap, bounds);
        DrawOuterBounds(tilemap, bounds);

        Handles.zTest = oldZTest;
    }

    private static void DrawGridLines(Tilemap tilemap, BoundsInt bounds)
    {
        Handles.color = GridLineColor;

        int minX = bounds.xMin;
        int maxX = bounds.xMax;
        int minY = bounds.yMin;
        int maxY = bounds.yMax;

        for (int x = minX; x <= maxX; x++)
        {
            Handles.DrawLine(
                CellCornerToWorld(tilemap, x, minY),
                CellCornerToWorld(tilemap, x, maxY)
            );
        }

        for (int y = minY; y <= maxY; y++)
        {
            Handles.DrawLine(
                CellCornerToWorld(tilemap, minX, y),
                CellCornerToWorld(tilemap, maxX, y)
            );
        }
    }

    private static void DrawOuterBounds(Tilemap tilemap, BoundsInt bounds)
    {
        Vector3 bottomLeft =
            CellCornerToWorld(tilemap, bounds.xMin, bounds.yMin);
        Vector3 bottomRight =
            CellCornerToWorld(tilemap, bounds.xMax, bounds.yMin);
        Vector3 topRight =
            CellCornerToWorld(tilemap, bounds.xMax, bounds.yMax);
        Vector3 topLeft =
            CellCornerToWorld(tilemap, bounds.xMin, bounds.yMax);

        Handles.color = BoundsColor;
        Handles.DrawAAPolyLine(
            4f,
            bottomLeft,
            bottomRight,
            topRight,
            topLeft,
            bottomLeft
        );
    }

    private static void DrawFilledCells(Tilemap tilemap, BoundsInt bounds)
    {
        int drawCount = 0;

        foreach (Vector3Int cell in bounds.allPositionsWithin)
        {
            if (!tilemap.HasTile(cell))
                continue;

            DrawCell(tilemap, cell);
            drawCount++;

            if (drawCount >= MaxFilledCellDrawCount)
                return;
        }
    }

    private static void DrawCell(Tilemap tilemap, Vector3Int cell)
    {
        Vector3 bottomLeft =
            CellCornerToWorld(tilemap, cell.x, cell.y);
        Vector3 bottomRight =
            CellCornerToWorld(tilemap, cell.x + 1, cell.y);
        Vector3 topRight =
            CellCornerToWorld(tilemap, cell.x + 1, cell.y + 1);
        Vector3 topLeft =
            CellCornerToWorld(tilemap, cell.x, cell.y + 1);

        Handles.DrawSolidRectangleWithOutline(
            new[] { bottomLeft, bottomRight, topRight, topLeft },
            FilledCellColor,
            FilledCellOutlineColor
        );
    }

    private static Vector3 CellCornerToWorld(
        Tilemap tilemap,
        int x,
        int y)
    {
        return tilemap.layoutGrid.CellToWorld(
            new Vector3Int(x, y, 0)
        );
    }
}
