using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// O(1) 格子坐标 → PushableObject 索引表。
/// 替代 OrganController.GetPushableAt() 的全表扫描。
/// 通过订阅 PushableObject.OnGridPositionChanged 自动保持同步。
/// </summary>
public class GridPositionIndex
{
    private readonly Dictionary<Vector3Int, PushableObject> map = new();

    /// <summary>注册一个物体，记录其初始位置。</summary>
    public void Register(PushableObject obj)
    {
        map[obj.GridPos] = obj;
    }

    /// <summary>批量注册。</summary>
    public void RegisterAll(IEnumerable<PushableObject> objects)
    {
        foreach (var obj in objects)
            map[obj.GridPos] = obj;
    }

    /// <summary>注销一个物体。</summary>
    public void Unregister(PushableObject obj)
    {
        // 需要找到并移除当前所有指向该物体的条目
        // 效率不高，但注销操作不频繁，用反向查找
        Vector3Int? keyToRemove = null;
        foreach (var kv in map)
        {
            if (kv.Value == obj)
            {
                keyToRemove = kv.Key;
                break;
            }
        }
        if (keyToRemove.HasValue)
            map.Remove(keyToRemove.Value);
    }

    /// <summary>物体移动时更新索引。</summary>
    public void OnMoved(PushableObject obj, Vector3Int oldPos, Vector3Int newPos)
    {
        map.Remove(oldPos);
        map[newPos] = obj;
    }

    /// <summary>O(1) 查询指定格子上的物体。</summary>
    public PushableObject GetAt(Vector3Int pos)
    {
        map.TryGetValue(pos, out var obj);
        return obj;
    }

    /// <summary>查询指定格子上的物体，排除指定对象。</summary>
    public PushableObject GetAt(Vector3Int pos, PushableObject exclude)
    {
        if (map.TryGetValue(pos, out var obj) && obj != exclude)
            return obj;
        return null;
    }

    /// <summary>清空索引。</summary>
    public void Clear()
    {
        map.Clear();
    }

    /// <summary>当前索引中的物体数量。</summary>
    public int Count => map.Count;

    /// <summary>获取索引中所有物体的集合（用于遍历）。</summary>
    public IEnumerable<PushableObject> AllObjects => map.Values;
}
