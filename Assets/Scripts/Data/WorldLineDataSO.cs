using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// 一个世界线的静态配置。
/// 
/// 世界线不是单纯的地面主题，而是一个“世界内容包”的入口：当前 MVP 只配置地面、
/// 掩体和少量测试敌人；未来可以继续加入独立波次、道具布局、世界机制和状态规则。
/// 运行时的敌人位置、当前波次等状态不保存在这个资产中。
/// </summary>
[CreateAssetMenu(fileName = "WorldLine", menuName = "RainsenVamp/World/World Line")]
public class WorldLineDataSO : ScriptableObject
{
    [Header("世界线标识")]
    [Tooltip("世界线的稳定 ID，不使用显示名称作为逻辑键。")]
    [SerializeField] private string worldLineId = "main";

    [Tooltip("未来本地化系统使用的世界线名称键。")]
    [SerializeField] private string displayNameKey = "world.main";

    [Header("地图内容")]
    [Tooltip("当前世界线使用的地面主题。")]
    [SerializeField] private MapThemeDataSO groundTheme;

    [Tooltip("当前世界线的掩体 Sprite。MVP 中所有掩体格使用同一张 Sprite。")]
    [SerializeField] private Sprite coverSprite;

    [Tooltip("以 16x16 区块为模板的掩体局部坐标。不同世界线使用不同列表。")]
    [SerializeField] private List<Vector2Int> coverCells = new List<Vector2Int>();

    [Tooltip("玩家周围多少个世界单位内不生成掩体，用于防止切换后玩家嵌入障碍。")]
    [SerializeField, Min(0)] private int safeRadiusInCells = 2;

    [Header("MVP 测试敌人")]
    [Tooltip("MVP 阶段固定生成的测试敌人 Prefab。完整版会替换为世界专属敌人池。")]
    [SerializeField] private GameObject testEnemyPrefab;

    [Tooltip("相对于玩家初始位置的测试敌人生成偏移。两个世界可以使用不同偏移。")]
    [SerializeField] private List<Vector2> testEnemySpawnOffsets = new List<Vector2>();

    [Header("世界波次")]
    [Tooltip("当前世界线使用的独立波次配置。运行时状态由 WorldWaveManager 保存。")]
    [SerializeField] private WaveConfigSO waveConfig;

    [System.NonSerialized] private TileBase runtimeCoverTile;

    public string WorldLineId => worldLineId;
    public string DisplayNameKey => displayNameKey;
    public MapThemeDataSO GroundTheme => groundTheme;
    public int SafeRadiusInCells => Mathf.Max(0, safeRadiusInCells);
    public GameObject TestEnemyPrefab => testEnemyPrefab;
    public int CoverCellCount => coverCells == null ? 0 : coverCells.Count;
    public int TestEnemyCount => testEnemySpawnOffsets == null ? 0 : testEnemySpawnOffsets.Count;
    public WaveConfigSO WaveConfig => waveConfig;

    /// <summary>
    /// 判断世界线是否具备 MVP 运行所需的完整配置。
    /// </summary>
    public bool IsValid
    {
        get
        {
            return !string.IsNullOrWhiteSpace(worldLineId)
                && groundTheme != null
                && groundTheme.IsValid
                && coverSprite != null
                && waveConfig != null
                && waveConfig.rules != null
                && waveConfig.rules.Count > 0;
        }
    }

    public Vector2Int GetCoverCell(int index)
    {
        if (coverCells == null || index < 0 || index >= coverCells.Count)
        {
            return default;
        }

        return coverCells[index];
    }

    public Vector2 GetTestEnemySpawnOffset(int index)
    {
        if (testEnemySpawnOffsets == null || index < 0 || index >= testEnemySpawnOffsets.Count)
        {
            return default;
        }

        return testEnemySpawnOffsets[index];
    }

    /// <summary>
    /// 获取当前世界线共享的运行时掩体 Tile。
    /// Tile 不写回 ScriptableObject 资产，只在运行时创建一次，供两个世界区块复用。
    /// </summary>
    public TileBase GetCoverTile()
    {
        if (coverSprite == null)
        {
            return null;
        }

        if (runtimeCoverTile == null)
        {
            Tile tile = ScriptableObject.CreateInstance<Tile>();
            tile.name = $"{worldLineId}_RuntimeCoverTile";
            tile.sprite = coverSprite;
            tile.colliderType = Tile.ColliderType.Grid;
            tile.hideFlags = HideFlags.HideAndDontSave;
            runtimeCoverTile = tile;
        }

        return runtimeCoverTile;
    }

    private void OnDisable()
    {
        if (runtimeCoverTile == null)
        {
            return;
        }

#if UNITY_EDITOR
        if (Application.isPlaying)
        {
            Object.Destroy(runtimeCoverTile);
        }
        else
        {
            Object.DestroyImmediate(runtimeCoverTile);
        }
#else
        Object.Destroy(runtimeCoverTile);
#endif

        runtimeCoverTile = null;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        safeRadiusInCells = Mathf.Max(0, safeRadiusInCells);
    }
#endif
}
