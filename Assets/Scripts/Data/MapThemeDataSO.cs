using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// 地图主题数据。
/// 
/// 该 ScriptableObject 只保存“可配置的地图内容”，不保存玩家本局所在区块、
/// 当前活动区块等运行时状态。这样草地、机械地板以及未来新增的地图主题，
/// 都可以复用同一套 MapSegment 与 MapStreamManager 运行时逻辑。
/// </summary>
[CreateAssetMenu(fileName = "MapTheme", menuName = "RainsenVamp/Map/Map Theme")]
public class MapThemeDataSO : ScriptableObject
{
    [Header("稳定标识")]
    [Tooltip("供系统识别的稳定 ID，不应使用本地化显示名称作为 ID。")]
    [SerializeField] private string themeId = "grass";

    [Tooltip("本地化系统未来使用的文本键。当前阶段只保存键，不直接保存显示文本。")]
    [SerializeField] private string displayNameKey = "map.theme.grass";

    [Header("地面 Tile 素材")]
    [Tooltip("一组可用于地面的 Sprite 变体。MapSegment 会在运行时将它们包装为无碰撞 Tile。")]
    [SerializeField] private Sprite[] groundSprites;

    [Header("区块参数")]
    [Tooltip("单个地图区块在 X 轴上的 Tile 数量。所有主题最好保持一致，切换时更稳定。")]
    [SerializeField, Min(1)] private int segmentWidth = 16;

    [Tooltip("单个地图区块在 Y 轴上的 Tile 数量。所有主题最好保持一致，切换时更稳定。")]
    [SerializeField, Min(1)] private int segmentHeight = 16;

    [Tooltip("区块内容的基础随机种子。同一区块坐标与同一主题应始终得到相同分布。")]
    [SerializeField] private int randomSeed = 104729;

    // 运行时缓存：Tilemap 需要 TileBase，而美术配置更适合直接引用 Sprite。
    // 这些 Tile 不写回资产，只在运行时为每个 Sprite 创建一次并被所有区块复用。
    [System.NonSerialized] private TileBase[] runtimeGroundTiles;

    /// <summary>
    /// 地图主题的稳定 ID。
    /// </summary>
    public string ThemeId => themeId;

    /// <summary>
    /// 本地化显示名称键。
    /// </summary>
    public string DisplayNameKey => displayNameKey;

    /// <summary>
    /// 单个区块的宽度，单位为 Tile 数量。
    /// </summary>
    public int SegmentWidth => Mathf.Max(1, segmentWidth);

    /// <summary>
    /// 单个区块的高度，单位为 Tile 数量。
    /// </summary>
    public int SegmentHeight => Mathf.Max(1, segmentHeight);

    /// <summary>
    /// 当前主题的随机种子。
    /// </summary>
    public int RandomSeed => randomSeed;

    /// <summary>
    /// 地面变体数量。
    /// </summary>
    public int GroundTileCount => groundSprites == null ? 0 : groundSprites.Length;

    /// <summary>
    /// 主题是否具备可供地图渲染的最小数据。
    /// </summary>
    public bool IsValid
    {
        get
        {
            if (string.IsNullOrWhiteSpace(themeId) || GroundTileCount == 0)
            {
                return false;
            }

            // 主题数组中只要存在空引用，就可能在 Tilemap 中产生不可见的缺口。
            // 在启动流式地图前提前判 invalid，比运行中才发现某个变体没有显示更容易定位。
            for (int i = 0; i < groundSprites.Length; i++)
            {
                if (groundSprites[i] == null)
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>
    /// 获取一个可供 Tilemap 使用的运行时 Tile。
    /// 
    /// 这里不把 Tile 资产硬编码到主题数据中，而是将 Sprite 转换为运行时 Tile。
    /// 这样一套素材可以直接被多个地图区块共享，避免每个区块都创建一份 Tile 对象。
    /// </summary>
    public TileBase GetGroundTile(int index)
    {
        if (index < 0 || index >= GroundTileCount)
        {
            return null;
        }

        EnsureRuntimeTiles();
        return runtimeGroundTiles[index];
    }

    private void EnsureRuntimeTiles()
    {
        if (runtimeGroundTiles != null && runtimeGroundTiles.Length == GroundTileCount)
        {
            return;
        }

        ReleaseRuntimeTiles();

        runtimeGroundTiles = new TileBase[GroundTileCount];
        for (int i = 0; i < GroundTileCount; i++)
        {
            Sprite sprite = groundSprites[i];
            if (sprite == null)
            {
                continue;
            }

            Tile tile = ScriptableObject.CreateInstance<Tile>();
            tile.name = $"{themeId}_RuntimeTile_{i}";
            tile.sprite = sprite;
            tile.colliderType = Tile.ColliderType.None;
            tile.hideFlags = HideFlags.HideAndDontSave;
            runtimeGroundTiles[i] = tile;
        }
    }

    private void ReleaseRuntimeTiles()
    {
        if (runtimeGroundTiles == null)
        {
            return;
        }

        for (int i = 0; i < runtimeGroundTiles.Length; i++)
        {
            if (runtimeGroundTiles[i] == null)
            {
                continue;
            }

#if UNITY_EDITOR
            if (Application.isPlaying)
            {
                Object.Destroy(runtimeGroundTiles[i]);
            }
            else
            {
                Object.DestroyImmediate(runtimeGroundTiles[i]);
            }
#else
            Object.Destroy(runtimeGroundTiles[i]);
#endif
        }

        runtimeGroundTiles = null;
    }

    private void OnDisable()
    {
        // 主题资产卸载时清理运行时创建的隐藏 Tile，避免编辑器反复进入/退出播放模式时积累对象。
        ReleaseRuntimeTiles();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        segmentWidth = Mathf.Max(1, segmentWidth);
        segmentHeight = Mathf.Max(1, segmentHeight);
    }
#endif
}
