# Session 6 代码改动详情

> **留档标准**：本文件按照 Session 4 的详细格式记录。新增 C# 文件提供完整正文；修改或删除的 C# 文件提供 Before/After；Prefab、Scene 和 ScriptableObject 记录实际变更的完整关键序列化块。
>
> **完整源码位置**：项目中的最终运行源码位于 `Assets/Scripts/`。本文正文用于复盘设计和变更过程，不替代 Unity 实际资产。

---

## 一、本次开发目标

Session 6 从“单地图主题切换”推进到 X 变量双世界线 MVP：

- 主世界与副世界同时运行。
- 两个世界共享同一套世界坐标。
- 玩家实体只与当前世界交互。
- 副世界敌人不停止 AI，而是继续追踪玩家位置。
- F 键切换玩家当前世界归属。
- 两个世界拥有不同的地面和掩体布局。

本次明确不实现：

- 世界独立波次。
- 世界状态快照。
- 玩家属性分支。
- 道具和拾取物的世界独立状态。
- 子弹与掩体碰撞。

---

## 二、新增文件

### 1. `Assets/Scripts/Data/MapThemeDataSO.cs` [新增文件]

**用途**：保存单个地图地面主题的可配置数据，并在运行时把 Sprite 变体包装为共享 Tile。

**设计要点**：

- ScriptableObject 只保存静态配置，不保存本局世界状态。
- 每个主题的运行时 Tile 只创建一次。
- 地面 Tile 明确设置为无碰撞。
- `IsValid` 会检查 Sprite 数组中的空引用，避免运行时出现不可见 Tile 缺口。

<details>
<summary>点击展开完整代码</summary>

```csharp
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
```

</details>

---

### 2. `Assets/Scripts/Data/WorldLineDataSO.cs` [新增文件]

**用途**：把地面、掩体和 MVP 测试敌人组合成一个世界线内容包。

**关键点**：世界线是静态配置；敌人运行位置、波次时间和拾取状态属于未来的运行时状态层。

<details>
<summary>点击展开完整代码</summary>

```csharp
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

    [System.NonSerialized] private TileBase runtimeCoverTile;

    public string WorldLineId => worldLineId;
    public string DisplayNameKey => displayNameKey;
    public MapThemeDataSO GroundTheme => groundTheme;
    public int SafeRadiusInCells => Mathf.Max(0, safeRadiusInCells);
    public GameObject TestEnemyPrefab => testEnemyPrefab;
    public int CoverCellCount => coverCells == null ? 0 : coverCells.Count;
    public int TestEnemyCount => testEnemySpawnOffsets == null ? 0 : testEnemySpawnOffsets.Count;

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
                && testEnemyPrefab != null
                && TestEnemyCount > 0;
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
```

</details>

---

### 3. `Assets/Scripts/Core/WorldEnemySimulation.cs` [新增文件]

**用途**：每个世界生成 3 只固定测试敌人，用来证明副世界 AI 继续运行。

<details>
<summary>点击展开完整代码</summary>

```csharp
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 世界线 MVP 测试敌人运行器。
///
/// 每个世界生成少量固定测试敌人。无论世界是否为当前交互世界，EnemyBase 都继续运行
/// 自己的 FixedUpdate 追踪玩家；WorldLineCoordinator 只开关它们的 Renderer 和 Collider。
/// 这样可以在不复制完整 WaveManager 的情况下证明副世界敌人确实持续移动。
/// </summary>
public class WorldEnemySimulation : MonoBehaviour
{
    private sealed class TrackedEnemy
    {
        public GameObject instance;
        public Collider2D[] colliders;
        public Renderer[] renderers;
    }

    [Header("世界配置")]
    [SerializeField] private WorldLineDataSO worldLine;
    [SerializeField] private Transform target;
    [SerializeField] private Transform entityRoot;

    private readonly List<TrackedEnemy> trackedEnemies = new List<TrackedEnemy>();
    private bool worldActive;
    private bool hasSpawned;

    public WorldLineDataSO WorldLine => worldLine;
    public int ActiveEnemyCount => CountActiveEnemies();

    private void Awake()
    {
        if (target == null)
        {
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player != null)
            {
                target = player.transform;
            }
        }

        if (entityRoot == null)
        {
            entityRoot = transform;
        }
    }

    private void Start()
    {
        if (!ValidateSetup())
        {
            return;
        }

        SpawnTestEnemies();
        ApplyWorldState();
    }

    /// <summary>
    /// 设置该世界是否是当前与玩家交互的世界。
    /// 关闭时只隐藏表现和 Collider，不停止敌人的 AI 更新。
    /// </summary>
    public void SetWorldActive(bool active)
    {
        worldActive = active;

        if (hasSpawned)
        {
            ApplyWorldState();
        }
    }

    private bool ValidateSetup()
    {
        if (worldLine == null || !worldLine.IsValid)
        {
            Debug.LogError("WorldEnemySimulation 缺少有效世界线配置。", this);
            return false;
        }

        if (target == null)
        {
            Debug.LogError("WorldEnemySimulation 缺少玩家跟踪目标。", this);
            return false;
        }

        if (PoolManager.Instance == null)
        {
            Debug.LogError("WorldEnemySimulation 找不到 PoolManager。", this);
            return false;
        }

        return true;
    }

    private void SpawnTestEnemies()
    {
        for (int i = 0; i < worldLine.TestEnemyCount; i++)
        {
            Vector2 offset = worldLine.GetTestEnemySpawnOffset(i);
            Vector3 spawnPosition = target.position + new Vector3(offset.x, offset.y, 0f);
            GameObject enemy = PoolManager.Instance.Spawn(
                worldLine.TestEnemyPrefab,
                spawnPosition,
                Quaternion.identity);

            if (enemy == null)
            {
                continue;
            }

            enemy.transform.SetParent(entityRoot, true);
            trackedEnemies.Add(new TrackedEnemy
            {
                instance = enemy,
                colliders = enemy.GetComponentsInChildren<Collider2D>(true),
                renderers = enemy.GetComponentsInChildren<Renderer>(true)
            });
        }

        hasSpawned = true;
    }

    private void ApplyWorldState()
    {
        for (int i = 0; i < trackedEnemies.Count; i++)
        {
            TrackedEnemy enemy = trackedEnemies[i];
            if (enemy.instance == null)
            {
                continue;
            }

            bool shouldInteract = worldActive && enemy.instance.activeInHierarchy;

            for (int j = 0; j < enemy.colliders.Length; j++)
            {
                enemy.colliders[j].enabled = shouldInteract;
            }

            for (int j = 0; j < enemy.renderers.Length; j++)
            {
                enemy.renderers[j].enabled = shouldInteract;
            }
        }
    }

    private int CountActiveEnemies()
    {
        int count = 0;
        for (int i = 0; i < trackedEnemies.Count; i++)
        {
            if (trackedEnemies[i].instance != null && trackedEnemies[i].instance.activeInHierarchy)
            {
                count++;
            }
        }

        return count;
    }
}
```

</details>

---

### 4. `Assets/Scripts/Core/WorldLineCoordinator.cs` [新增文件]

**用途**：维护主世界/副世界的激活状态，并把 F 键转换为世界归属切换。

<details>
<summary>点击展开完整代码</summary>

```csharp
using UnityEngine;

/// <summary>
/// 双世界线 MVP 的总协调器。
///
/// 主世界和副世界各自拥有独立的地图流和测试敌人集合，但共享玩家坐标。
/// 协调器只负责决定哪一套世界内容对玩家开放 Renderer/Collider；不销毁或暂停
/// 副世界对象，因此副世界敌人可以继续追踪玩家位置。
/// </summary>
public class WorldLineCoordinator : MonoBehaviour
{
    [System.Serializable]
    private class WorldSlot
    {
        public WorldLineDataSO worldLine;
        public MapStreamManager mapStreamManager;
        public WorldEnemySimulation enemySimulation;

        public void ApplyActiveState(bool active)
        {
            if (mapStreamManager != null)
            {
                mapStreamManager.SetPresentationActive(active);
                mapStreamManager.SetInteractionActive(active);
            }

            if (enemySimulation != null)
            {
                enemySimulation.SetWorldActive(active);
            }
        }
    }

    [Header("世界上下文")]
    [SerializeField] private WorldSlot mainWorld;
    [SerializeField] private WorldSlot subWorld;
    [SerializeField] private Transform player;

    [Header("MVP 调试输入")]
    [SerializeField] private KeyCode switchKey = KeyCode.F;
    [SerializeField] private bool ignoreWhenPaused = true;

    private bool mainWorldIsActive = true;

    public WorldLineDataSO ActiveWorldLine => mainWorldIsActive ? mainWorld.worldLine : subWorld.worldLine;
    public bool MainWorldIsActive => mainWorldIsActive;

    private void Awake()
    {
        if (player == null)
        {
            GameObject playerObject = GameObject.FindGameObjectWithTag("Player");
            if (playerObject != null)
            {
                player = playerObject.transform;
            }
        }

        if (!ValidateSetup())
        {
            return;
        }

        // 先设置状态，再等待两个世界各自 Start 生成区块和敌人。
        // 这样副世界在生成完成后会自动保持隐藏且不可交互，但仍会运行 AI。
        ApplyWorldStates();
    }

    private void Update()
    {
        if (!Input.GetKeyDown(switchKey))
        {
            return;
        }

        if (ignoreWhenPaused && Time.timeScale <= 0f)
        {
            return;
        }

        SwitchWorldLine();
    }

    /// <summary>
    /// 切换玩家当前归属的世界线。
    /// 玩家 Transform、PlayerStats、武器和经验不会被重置。
    /// </summary>
    public void SwitchWorldLine()
    {
        mainWorldIsActive = !mainWorldIsActive;
        ApplyWorldStates();

        WorldLineDataSO activeWorld = ActiveWorldLine;
        if (activeWorld != null)
        {
            Debug.Log($"玩家世界线已切换为：{activeWorld.WorldLineId}。", this);
        }
    }

    private void ApplyWorldStates()
    {
        mainWorld.ApplyActiveState(mainWorldIsActive);
        subWorld.ApplyActiveState(!mainWorldIsActive);
    }

    private bool ValidateSetup()
    {
        bool valid = true;

        if (player == null)
        {
            Debug.LogError("WorldLineCoordinator 找不到 Player。", this);
            valid = false;
        }

        if (mainWorld == null || mainWorld.worldLine == null || mainWorld.mapStreamManager == null || mainWorld.enemySimulation == null)
        {
            Debug.LogError("WorldLineCoordinator 的主世界上下文配置不完整。", this);
            valid = false;
        }

        if (subWorld == null || subWorld.worldLine == null || subWorld.mapStreamManager == null || subWorld.enemySimulation == null)
        {
            Debug.LogError("WorldLineCoordinator 的副世界上下文配置不完整。", this);
            valid = false;
        }

        return valid;
    }
}
```

</details>

---

## 三、修改文件 Before/After

### 1. `Assets/Scripts/Core/MapSegment.cs`

**修改原因**：从“只绘制地面”扩展为“同时绘制地面与世界专属掩体”，并增加世界表现/交互开关。

#### Before：地面单层版本完整源码

```csharp
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// 一个有限尺寸的地图区块。
///
/// MapSegment 不负责决定自己应该处于哪个世界坐标，也不负责追踪玩家。
/// 它只接收 MapStreamManager 下发的主题和区块坐标，然后把对应的地面 Tile 写入 Tilemap。
/// 这种单一职责让区块可以被重复回收和复用，而不会与全局流式调度耦合。
/// </summary>
[DisallowMultipleComponent]
public class MapSegment : MonoBehaviour
{
    [Header("Tilemap 引用")]
    [Tooltip("用于绘制当前区块地面的 Tilemap。为空时会尝试从当前 GameObject 自动获取。")]
    [SerializeField] private Tilemap groundTilemap;

    private TileBase[] tileBuffer;
    private MapThemeDataSO currentTheme;
    private Vector2Int coordinate;

    public Vector2Int Coordinate => coordinate;
    public MapThemeDataSO CurrentTheme => currentTheme;

    private void Awake()
    {
        ResolveGroundTilemap();
    }

    public bool Initialize(MapThemeDataSO theme, Vector2Int segmentCoordinate, Vector3 worldPosition)
    {
        ResolveGroundTilemap();

        if (theme == null)
        {
            Debug.LogError($"{nameof(MapSegment)} 初始化失败：收到的地图主题为空。", this);
            return false;
        }

        if (!theme.IsValid)
        {
            Debug.LogError($"{nameof(MapSegment)} 初始化失败：主题 {theme.name} 没有有效的地面 Sprite。", theme);
            return false;
        }

        if (groundTilemap == null)
        {
            Debug.LogError($"{nameof(MapSegment)} 初始化失败：未找到 Ground Tilemap。", this);
            return false;
        }

        int width = theme.SegmentWidth;
        int height = theme.SegmentHeight;
        int requiredTileCount = width * height;

        EnsureTileBuffer(requiredTileCount);
        FillTileBuffer(theme, segmentCoordinate, width, height);

        coordinate = segmentCoordinate;
        currentTheme = theme;
        transform.position = worldPosition;

        groundTilemap.ClearAllTiles();
        groundTilemap.SetTilesBlock(new BoundsInt(0, 0, 0, width, height, 1), tileBuffer);
        gameObject.name = $"MapSegment_{segmentCoordinate.x}_{segmentCoordinate.y}";

        return true;
    }

    public void Clear()
    {
        if (groundTilemap != null)
        {
            groundTilemap.ClearAllTiles();
        }

        currentTheme = null;
    }

    private void ResolveGroundTilemap()
    {
        if (groundTilemap == null)
        {
            groundTilemap = GetComponent<Tilemap>();
        }
    }

    private void EnsureTileBuffer(int requiredTileCount)
    {
        if (tileBuffer == null || tileBuffer.Length != requiredTileCount)
        {
            tileBuffer = new TileBase[requiredTileCount];
        }
    }

    private void FillTileBuffer(MapThemeDataSO theme, Vector2Int segmentCoordinate, int width, int height)
    {
        int tileVariantCount = theme.GroundTileCount;
        int bufferIndex = 0;

        for (int localY = 0; localY < height; localY++)
        {
            for (int localX = 0; localX < width; localX++)
            {
                int tileIndex = GetDeterministicTileIndex(
                    segmentCoordinate,
                    localX,
                    localY,
                    theme.RandomSeed,
                    tileVariantCount);

                tileBuffer[bufferIndex++] = theme.GetGroundTile(tileIndex);
            }
        }
    }

    private static int GetDeterministicTileIndex(
        Vector2Int segmentCoordinate,
        int localX,
        int localY,
        int themeSeed,
        int tileVariantCount)
    {
        if (tileVariantCount <= 1)
        {
            return 0;
        }

        unchecked
        {
            int worldCellX = segmentCoordinate.x * 397 + localX;
            int worldCellY = segmentCoordinate.y * 397 + localY;
            uint hash = (uint)themeSeed;
            hash ^= (uint)worldCellX * 374761393u;
            hash = (hash << 13) | (hash >> 19);
            hash ^= (uint)worldCellY * 668265263u;
            hash *= 1274126177u;
            hash ^= hash >> 16;
            return (int)(hash % (uint)tileVariantCount);
        }
    }
}
```

#### After：双层世界区块完整源码

```csharp
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// 一个可复用的有限地图区块。
///
/// 区块同时绘制地面层和掩体层。它不决定世界线、不追踪玩家，也不决定自己何时处于
/// 主世界或副世界；这些职责由 MapStreamManager 与 WorldLineCoordinator 负责。
/// </summary>
[DisallowMultipleComponent]
public class MapSegment : MonoBehaviour
{
    [Header("地面层")]
    [SerializeField] private Tilemap groundTilemap;
    [SerializeField] private TilemapRenderer groundRenderer;

    [Header("掩体层")]
    [SerializeField] private Tilemap coverTilemap;
    [SerializeField] private TilemapRenderer coverRenderer;
    [SerializeField] private TilemapCollider2D coverCollider;

    private TileBase[] groundTileBuffer;
    private TileBase[] coverTileBuffer;
    private WorldLineDataSO currentWorldLine;
    private Vector2Int coordinate;
    private bool presentationActive = true;
    private bool interactionActive = true;

    public Vector2Int Coordinate => coordinate;
    public WorldLineDataSO CurrentWorldLine => currentWorldLine;

    private void Awake()
    {
        ResolveComponents();
    }

    /// <summary>
    /// 根据世界线配置重绘整个区块。
    /// </summary>
    public bool Initialize(
        WorldLineDataSO worldLine,
        Vector2Int segmentCoordinate,
        Vector3 worldPosition,
        Vector3 protectedWorldPosition)
    {
        ResolveComponents();

        if (worldLine == null || !worldLine.IsValid)
        {
            Debug.LogError($"{nameof(MapSegment)} 初始化失败：世界线配置无效。", this);
            return false;
        }

        if (groundTilemap == null || groundRenderer == null)
        {
            Debug.LogError($"{nameof(MapSegment)} 初始化失败：地面 Tilemap 或 Renderer 缺失。", this);
            return false;
        }

        if (coverTilemap == null || coverRenderer == null || coverCollider == null)
        {
            Debug.LogError($"{nameof(MapSegment)} 初始化失败：掩体 Tilemap、Renderer 或 Collider 缺失。", this);
            return false;
        }

        MapThemeDataSO groundTheme = worldLine.GroundTheme;
        int width = groundTheme.SegmentWidth;
        int height = groundTheme.SegmentHeight;
        int tileCount = width * height;

        EnsureBuffers(tileCount);
        FillGroundBuffer(groundTheme, segmentCoordinate, width, height);
        FillCoverBuffer(worldLine, segmentCoordinate, worldPosition, protectedWorldPosition, width, height);

        coordinate = segmentCoordinate;
        currentWorldLine = worldLine;
        transform.position = worldPosition;

        groundTilemap.ClearAllTiles();
        coverTilemap.ClearAllTiles();
        BoundsInt bounds = new BoundsInt(0, 0, 0, width, height, 1);
        groundTilemap.SetTilesBlock(bounds, groundTileBuffer);
        coverTilemap.SetTilesBlock(bounds, coverTileBuffer);

        SetPresentationActive(presentationActive);
        SetInteractionActive(interactionActive);
        gameObject.name = $"MapSegment_{worldLine.WorldLineId}_{segmentCoordinate.x}_{segmentCoordinate.y}";
        return true;
    }

    public void SetPresentationActive(bool active)
    {
        presentationActive = active;

        if (groundRenderer != null)
        {
            groundRenderer.enabled = active;
        }

        if (coverRenderer != null)
        {
            coverRenderer.enabled = active;
        }
    }

    public void SetInteractionActive(bool active)
    {
        interactionActive = active;

        if (coverCollider != null)
        {
            coverCollider.enabled = active;
        }
    }

    public void Clear()
    {
        if (groundTilemap != null)
        {
            groundTilemap.ClearAllTiles();
        }

        if (coverTilemap != null)
        {
            coverTilemap.ClearAllTiles();
        }

        SetInteractionActive(false);
        currentWorldLine = null;
    }

    private void ResolveComponents()
    {
        if (groundTilemap == null)
        {
            groundTilemap = GetComponent<Tilemap>();
        }

        if (groundRenderer == null && groundTilemap != null)
        {
            groundRenderer = groundTilemap.GetComponent<TilemapRenderer>();
        }

        if (coverTilemap != null)
        {
            if (coverRenderer == null)
            {
                coverRenderer = coverTilemap.GetComponent<TilemapRenderer>();
            }

            if (coverCollider == null)
            {
                coverCollider = coverTilemap.GetComponent<TilemapCollider2D>();
            }
        }
    }

    private void EnsureBuffers(int tileCount)
    {
        if (groundTileBuffer == null || groundTileBuffer.Length != tileCount)
        {
            groundTileBuffer = new TileBase[tileCount];
        }

        if (coverTileBuffer == null || coverTileBuffer.Length != tileCount)
        {
            coverTileBuffer = new TileBase[tileCount];
        }
    }

    private void FillGroundBuffer(MapThemeDataSO theme, Vector2Int segmentCoordinate, int width, int height)
    {
        int variantCount = theme.GroundTileCount;
        int bufferIndex = 0;

        for (int localY = 0; localY < height; localY++)
        {
            for (int localX = 0; localX < width; localX++)
            {
                int tileIndex = GetDeterministicTileIndex(
                    segmentCoordinate,
                    localX,
                    localY,
                    theme.RandomSeed,
                    variantCount);

                groundTileBuffer[bufferIndex++] = theme.GetGroundTile(tileIndex);
            }
        }
    }

    private void FillCoverBuffer(
        WorldLineDataSO worldLine,
        Vector2Int segmentCoordinate,
        Vector3 worldPosition,
        Vector3 protectedWorldPosition,
        int width,
        int height)
    {
        System.Array.Clear(coverTileBuffer, 0, coverTileBuffer.Length);
        TileBase coverTile = worldLine.GetCoverTile();
        float safeRadius = worldLine.SafeRadiusInCells + 0.5f;
        float safeRadiusSqr = safeRadius * safeRadius;

        for (int i = 0; i < worldLine.CoverCellCount; i++)
        {
            Vector2Int cell = worldLine.GetCoverCell(i);
            if (cell.x < 0 || cell.x >= width || cell.y < 0 || cell.y >= height)
            {
                continue;
            }

            Vector3 cellCenter = worldPosition + new Vector3(cell.x + 0.5f, cell.y + 0.5f, 0f);
            if ((cellCenter - protectedWorldPosition).sqrMagnitude <= safeRadiusSqr)
            {
                // 世界切换时保留玩家脚下的安全空间，避免新世界掩体嵌入玩家 Collider。
                continue;
            }

            coverTileBuffer[cell.y * width + cell.x] = coverTile;
        }
    }

    private static int GetDeterministicTileIndex(
        Vector2Int segmentCoordinate,
        int localX,
        int localY,
        int themeSeed,
        int variantCount)
    {
        if (variantCount <= 1)
        {
            return 0;
        }

        unchecked
        {
            int worldCellX = segmentCoordinate.x * 397 + localX;
            int worldCellY = segmentCoordinate.y * 397 + localY;
            uint hash = (uint)themeSeed;
            hash ^= (uint)worldCellX * 374761393u;
            hash = (hash << 13) | (hash >> 19);
            hash ^= (uint)worldCellY * 668265263u;
            hash *= 1274126177u;
            hash ^= hash >> 16;
            return (int)(hash % (uint)variantCount);
        }
    }
}
```

#### 修改总结

| 维度 | Before | After |
|---|---|---|
| 数据输入 | `MapThemeDataSO` | `WorldLineDataSO`，内部再引用地面主题 |
| Tilemap | 只有 Ground | Ground + Cover |
| 碰撞 | 无地图碰撞 | Cover `TilemapCollider2D` |
| 世界表现 | 默认始终显示 | `SetPresentationActive()` 控制 |
| 世界交互 | 无状态开关 | `SetInteractionActive()` 控制掩体 Collider |
| 玩家保护 | 无 | 切换/生成时保留安全半径 |

---

### 2. `Assets/Scripts/Core/MapStreamManager.cs`

**修改原因**：一个 MainLevel 中同时运行两套地图流，每套流属于一个独立世界。

#### Before：单主题 MapStreamManager 完整源码

```csharp
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 无限地图流式调度器。
///
/// “无限”在这里不是创建无限数量的 Tile，而是预先创建一个有限的区块窗口，
/// 并在玩家跨过区块边界时，把离开视野的区块重新摆到前方。这样活动区块数量和
/// Tilemap 内存占用都保持稳定，同时还可以在运行时切换地图主题。
/// </summary>
public class MapStreamManager : MonoBehaviour
{
    [Header("核心引用")]
    [Tooltip("用于重复复用的区块预制体。该预制体根节点必须挂载 MapSegment。")]
    [SerializeField] private MapSegment mapSegmentPrefab;

    [Tooltip("场景开始时使用的初始地图主题。")]
    [SerializeField] private MapThemeDataSO initialTheme;

    [Tooltip("用于计算当前区块坐标的跟踪目标，通常是玩家根节点。")]
    [SerializeField] private Transform target;

    [Header("流式窗口")]
    [Tooltip("玩家周围各方向保留的区块层数。1 代表 3x3，2 代表 5x5。")]
    [SerializeField, Min(0)] private int activeRadiusInSegments = 1;

    [Tooltip("是否在 Scene 视图中绘制活动区块边界。")]
    [SerializeField] private bool drawDebugGizmos;

    private readonly Dictionary<Vector2Int, MapSegment> activeSegments = new Dictionary<Vector2Int, MapSegment>(9);
    private readonly HashSet<Vector2Int> desiredCoordinates = new HashSet<Vector2Int>();
    private readonly List<Vector2Int> staleCoordinates = new List<Vector2Int>(9);
    private readonly List<MapSegment> freeSegments = new List<MapSegment>(9);

    private MapSegment[] segmentPool;
    private MapThemeDataSO currentTheme;
    private Vector2 segmentWorldSize;
    private Vector2Int currentCenterCoordinate;
    private bool initialized;

    public MapThemeDataSO CurrentTheme => currentTheme;
    public int ActiveSegmentCount => activeSegments.Count;

    private void Awake()
    {
        activeRadiusInSegments = Mathf.Max(0, activeRadiusInSegments);
        currentTheme = initialTheme;

        if (target == null)
        {
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player != null)
            {
                target = player.transform;
            }
        }

        if (!ValidateSetup())
        {
            return;
        }

        PrewarmSegments();
    }

    private void Start()
    {
        if (!ValidateSetup())
        {
            return;
        }

        InitializeStream();
    }

    private void Update()
    {
        if (!initialized || target == null)
        {
            return;
        }

        Vector2Int newCenterCoordinate = WorldToSegmentCoordinate(target.position);
        if (newCenterCoordinate == currentCenterCoordinate)
        {
            return;
        }

        RefreshVisibleSegments(forceRepaint: false);
    }

    public bool SwitchTheme(MapThemeDataSO newTheme)
    {
        if (newTheme == null || !newTheme.IsValid)
        {
            Debug.LogError("MapStreamManager.SwitchTheme 收到无效地图主题，已拒绝切换。", this);
            return false;
        }

        if (currentTheme == newTheme && initialized)
        {
            return true;
        }

        currentTheme = newTheme;
        segmentWorldSize = new Vector2(currentTheme.SegmentWidth, currentTheme.SegmentHeight);

        if (initialized)
        {
            RefreshVisibleSegments(forceRepaint: true);
        }

        return true;
    }

    private bool ValidateSetup()
    {
        bool isValid = true;

        if (mapSegmentPrefab == null)
        {
            Debug.LogError("MapStreamManager 缺少 MapSegment Prefab 引用。", this);
            isValid = false;
        }

        if (currentTheme == null || !currentTheme.IsValid)
        {
            Debug.LogError("MapStreamManager 缺少有效的初始地图主题或主题没有地面 Sprite。", this);
            isValid = false;
        }

        if (target == null)
        {
            Debug.LogError("MapStreamManager 缺少地图跟踪目标，请绑定 Player Transform 或添加 Player Tag。", this);
            isValid = false;
        }

        return isValid;
    }

    private void InitializeStream()
    {
        segmentWorldSize = new Vector2(currentTheme.SegmentWidth, currentTheme.SegmentHeight);
        currentCenterCoordinate = WorldToSegmentCoordinate(target.position);
        initialized = true;
        RefreshVisibleSegments(forceRepaint: true);
    }

    private void PrewarmSegments()
    {
        int diameter = activeRadiusInSegments * 2 + 1;
        int segmentCount = diameter * diameter;
        segmentPool = new MapSegment[segmentCount];

        for (int i = 0; i < segmentCount; i++)
        {
            MapSegment segment = Instantiate(mapSegmentPrefab, transform);
            segment.gameObject.SetActive(false);
            segmentPool[i] = segment;
            freeSegments.Add(segment);
        }
    }

    private void RefreshVisibleSegments(bool forceRepaint)
    {
        if (currentTheme == null || target == null || segmentWorldSize.x <= 0f || segmentWorldSize.y <= 0f)
        {
            return;
        }

        currentCenterCoordinate = WorldToSegmentCoordinate(target.position);
        desiredCoordinates.Clear();

        for (int y = -activeRadiusInSegments; y <= activeRadiusInSegments; y++)
        {
            for (int x = -activeRadiusInSegments; x <= activeRadiusInSegments; x++)
            {
                desiredCoordinates.Add(new Vector2Int(
                    currentCenterCoordinate.x + x,
                    currentCenterCoordinate.y + y));
            }
        }

        staleCoordinates.Clear();
        foreach (KeyValuePair<Vector2Int, MapSegment> pair in activeSegments)
        {
            if (!desiredCoordinates.Contains(pair.Key))
            {
                staleCoordinates.Add(pair.Key);
            }
        }

        for (int i = 0; i < staleCoordinates.Count; i++)
        {
            RecycleSegment(staleCoordinates[i]);
        }

        foreach (Vector2Int coordinate in desiredCoordinates)
        {
            if (activeSegments.TryGetValue(coordinate, out MapSegment existingSegment))
            {
                if (forceRepaint)
                {
                    ConfigureSegment(existingSegment, coordinate);
                }

                continue;
            }

            if (freeSegments.Count == 0)
            {
                Debug.LogError("MapStreamManager 没有可复用的 MapSegment，活动窗口容量计算可能不正确。", this);
                return;
            }

            int lastFreeIndex = freeSegments.Count - 1;
            MapSegment reusableSegment = freeSegments[lastFreeIndex];
            freeSegments.RemoveAt(lastFreeIndex);

            if (!ConfigureSegment(reusableSegment, coordinate))
            {
                reusableSegment.gameObject.SetActive(false);
                freeSegments.Add(reusableSegment);
                return;
            }

            activeSegments.Add(coordinate, reusableSegment);
        }
    }

    private bool ConfigureSegment(MapSegment segment, Vector2Int coordinate)
    {
        Vector3 worldPosition = new Vector3(
            coordinate.x * segmentWorldSize.x,
            coordinate.y * segmentWorldSize.y,
            0f);

        if (!segment.Initialize(currentTheme, coordinate, worldPosition))
        {
            return false;
        }

        segment.gameObject.SetActive(true);
        return true;
    }

    private void RecycleSegment(Vector2Int coordinate)
    {
        if (!activeSegments.TryGetValue(coordinate, out MapSegment segment))
        {
            return;
        }

        segment.Clear();
        segment.gameObject.SetActive(false);
        activeSegments.Remove(coordinate);
        freeSegments.Add(segment);
    }

    private Vector2Int WorldToSegmentCoordinate(Vector3 worldPosition)
    {
        return new Vector2Int(
            Mathf.FloorToInt(worldPosition.x / segmentWorldSize.x),
            Mathf.FloorToInt(worldPosition.y / segmentWorldSize.y));
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (!drawDebugGizmos || segmentWorldSize.x <= 0f || segmentWorldSize.y <= 0f)
        {
            return;
        }

        Gizmos.color = new Color(0.2f, 0.9f, 1f, 0.8f);
        foreach (KeyValuePair<Vector2Int, MapSegment> pair in activeSegments)
        {
            Vector3 center = new Vector3(
                (pair.Key.x + 0.5f) * segmentWorldSize.x,
                (pair.Key.y + 0.5f) * segmentWorldSize.y,
                0f);
            Gizmos.DrawWireCube(center, new Vector3(segmentWorldSize.x, segmentWorldSize.y, 0f));
        }
    }
#endif
}
```

#### After：双世界上下文版本完整源码

```csharp
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 一个世界线自己的地图流式管理器。
///
/// MVP 中主世界和副世界各持有一个 MapStreamManager。两个管理器都持续跟踪同一个
/// 玩家坐标并维护自己的 3x3 区块窗口；WorldLineCoordinator 只切换它们的表现和掩体交互，
/// 不停止副世界的区块和敌人运行。
/// </summary>
public class MapStreamManager : MonoBehaviour
{
    [Header("世界线配置")]
    [SerializeField] private MapSegment mapSegmentPrefab;
    [SerializeField] private WorldLineDataSO initialWorldLine;
    [SerializeField] private Transform target;

    [Header("流式窗口")]
    [SerializeField, Min(0)] private int activeRadiusInSegments = 1;
    [SerializeField] private bool drawDebugGizmos;

    private readonly Dictionary<Vector2Int, MapSegment> activeSegments = new Dictionary<Vector2Int, MapSegment>(9);
    private readonly HashSet<Vector2Int> desiredCoordinates = new HashSet<Vector2Int>();
    private readonly List<Vector2Int> staleCoordinates = new List<Vector2Int>(9);
    private readonly List<MapSegment> freeSegments = new List<MapSegment>(9);

    private MapSegment[] segmentPool;
    private WorldLineDataSO currentWorldLine;
    private Vector2 segmentWorldSize;
    private Vector2Int currentCenterCoordinate;
    private bool presentationActive = true;
    private bool interactionActive = true;
    private bool initialized;

    public WorldLineDataSO CurrentWorldLine => currentWorldLine;
    public int ActiveSegmentCount => activeSegments.Count;

    private void Awake()
    {
        activeRadiusInSegments = Mathf.Max(0, activeRadiusInSegments);
        currentWorldLine = initialWorldLine;

        if (target == null)
        {
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player != null)
            {
                target = player.transform;
            }
        }

        if (!ValidateSetup())
        {
            return;
        }

        PrewarmSegments();
    }

    private void Start()
    {
        if (!ValidateSetup())
        {
            return;
        }

        InitializeStream();
    }

    private void Update()
    {
        if (!initialized || target == null)
        {
            return;
        }

        Vector2Int newCenterCoordinate = WorldToSegmentCoordinate(target.position);
        if (newCenterCoordinate == currentCenterCoordinate)
        {
            return;
        }

        RefreshVisibleSegments();
    }

    /// <summary>
    /// 替换这个世界上下文使用的世界线配置。
    /// MVP 不通过它切换主/副世界，保留该 API 是为了后续世界内容热切换扩展。
    /// </summary>
    public bool SwitchWorldLine(WorldLineDataSO newWorldLine)
    {
        if (newWorldLine == null || !newWorldLine.IsValid)
        {
            Debug.LogError("MapStreamManager.SwitchWorldLine 收到无效世界线。", this);
            return false;
        }

        currentWorldLine = newWorldLine;
        segmentWorldSize = new Vector2(
            currentWorldLine.GroundTheme.SegmentWidth,
            currentWorldLine.GroundTheme.SegmentHeight);

        if (initialized)
        {
            RefreshVisibleSegments();
        }

        return true;
    }

    /// <summary>
    /// 只关闭当前世界的视觉表现，不停止区块流式更新。
    /// </summary>
    public void SetPresentationActive(bool active)
    {
        presentationActive = active;
        foreach (KeyValuePair<Vector2Int, MapSegment> pair in activeSegments)
        {
            pair.Value.SetPresentationActive(active);
        }
    }

    /// <summary>
    /// 只控制当前世界掩体的交互 Collider，不停止地图和 AI 运行。
    /// </summary>
    public void SetInteractionActive(bool active)
    {
        interactionActive = active;
        foreach (KeyValuePair<Vector2Int, MapSegment> pair in activeSegments)
        {
            pair.Value.SetInteractionActive(active);
        }
    }

    private bool ValidateSetup()
    {
        bool valid = true;

        if (mapSegmentPrefab == null)
        {
            Debug.LogError("MapStreamManager 缺少 MapSegment Prefab 引用。", this);
            valid = false;
        }

        if (currentWorldLine == null || !currentWorldLine.IsValid)
        {
            Debug.LogError("MapStreamManager 缺少有效的初始世界线配置。", this);
            valid = false;
        }

        if (target == null)
        {
            Debug.LogError("MapStreamManager 缺少地图跟踪目标。", this);
            valid = false;
        }

        return valid;
    }

    private void InitializeStream()
    {
        segmentWorldSize = new Vector2(
            currentWorldLine.GroundTheme.SegmentWidth,
            currentWorldLine.GroundTheme.SegmentHeight);
        currentCenterCoordinate = WorldToSegmentCoordinate(target.position);
        initialized = true;
        RefreshVisibleSegments();
    }

    private void PrewarmSegments()
    {
        int diameter = activeRadiusInSegments * 2 + 1;
        int segmentCount = diameter * diameter;
        segmentPool = new MapSegment[segmentCount];

        // 每个世界只在启动时预创建固定数量区块；跨区块移动只复用它们。
        for (int i = 0; i < segmentCount; i++)
        {
            MapSegment segment = Instantiate(mapSegmentPrefab, transform);
            segment.gameObject.SetActive(false);
            segmentPool[i] = segment;
            freeSegments.Add(segment);
        }
    }

    private void RefreshVisibleSegments()
    {
        if (currentWorldLine == null || target == null)
        {
            return;
        }

        currentCenterCoordinate = WorldToSegmentCoordinate(target.position);
        desiredCoordinates.Clear();

        for (int y = -activeRadiusInSegments; y <= activeRadiusInSegments; y++)
        {
            for (int x = -activeRadiusInSegments; x <= activeRadiusInSegments; x++)
            {
                desiredCoordinates.Add(new Vector2Int(
                    currentCenterCoordinate.x + x,
                    currentCenterCoordinate.y + y));
            }
        }

        staleCoordinates.Clear();
        foreach (KeyValuePair<Vector2Int, MapSegment> pair in activeSegments)
        {
            if (!desiredCoordinates.Contains(pair.Key))
            {
                staleCoordinates.Add(pair.Key);
            }
        }

        for (int i = 0; i < staleCoordinates.Count; i++)
        {
            RecycleSegment(staleCoordinates[i]);
        }

        foreach (Vector2Int coordinate in desiredCoordinates)
        {
            if (activeSegments.ContainsKey(coordinate))
            {
                continue;
            }

            if (freeSegments.Count == 0)
            {
                Debug.LogError("MapStreamManager 没有可复用的 MapSegment。", this);
                return;
            }

            int lastIndex = freeSegments.Count - 1;
            MapSegment segment = freeSegments[lastIndex];
            freeSegments.RemoveAt(lastIndex);

            if (!ConfigureSegment(segment, coordinate))
            {
                segment.gameObject.SetActive(false);
                freeSegments.Add(segment);
                return;
            }

            activeSegments.Add(coordinate, segment);
        }
    }

    private bool ConfigureSegment(MapSegment segment, Vector2Int coordinate)
    {
        Vector3 worldPosition = new Vector3(
            coordinate.x * segmentWorldSize.x,
            coordinate.y * segmentWorldSize.y,
            0f);

        if (!segment.Initialize(currentWorldLine, coordinate, worldPosition, target.position))
        {
            return false;
        }

        segment.SetPresentationActive(presentationActive);
        segment.SetInteractionActive(interactionActive);
        segment.gameObject.SetActive(true);
        return true;
    }

    private void RecycleSegment(Vector2Int coordinate)
    {
        if (!activeSegments.TryGetValue(coordinate, out MapSegment segment))
        {
            return;
        }

        segment.Clear();
        segment.gameObject.SetActive(false);
        activeSegments.Remove(coordinate);
        freeSegments.Add(segment);
    }

    private Vector2Int WorldToSegmentCoordinate(Vector3 worldPosition)
    {
        return new Vector2Int(
            Mathf.FloorToInt(worldPosition.x / segmentWorldSize.x),
            Mathf.FloorToInt(worldPosition.y / segmentWorldSize.y));
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (!drawDebugGizmos || segmentWorldSize.x <= 0f || segmentWorldSize.y <= 0f)
        {
            return;
        }

        Gizmos.color = new Color(0.2f, 0.9f, 1f, 0.8f);
        foreach (KeyValuePair<Vector2Int, MapSegment> pair in activeSegments)
        {
            Vector3 center = new Vector3(
                (pair.Key.x + 0.5f) * segmentWorldSize.x,
                (pair.Key.y + 0.5f) * segmentWorldSize.y,
                0f);
            Gizmos.DrawWireCube(center, new Vector3(segmentWorldSize.x, segmentWorldSize.y, 0f));
        }
    }
#endif
}
```

#### 修改总结

| 修改点 | Before | After |
|---|---|---|
| 世界数据 | 单个 `MapThemeDataSO` | 单个 `WorldLineDataSO` |
| 运行实例 | 场景中一套地图流 | 主世界、副世界各一套地图流 |
| 副世界 | 不存在 | 地图区块持续更新，表现和掩体交互可关闭 |
| 区块重绘 | 主题切换时重绘 | 每个世界根据自身世界线生成 |
| 输入职责 | `MapThemeDebugSwitcher` 直接控制地图 | `WorldLineCoordinator` 控制世界归属 |

---

### 3. `Assets/Scripts/Core/MapThemeDebugSwitcher.cs` [删除文件]

**删除原因**：旧脚本的职责是单世界 Grass/Mechanical 主题切换；双世界 MVP 改为由 `WorldLineCoordinator` 同时管理两个世界上下文。

#### Before：删除前完整源码

```csharp
using UnityEngine;

/// <summary>
/// 地图主题调试切换器。
///
/// 该组件只负责把临时测试输入转换为 MapStreamManager.SwitchTheme() 调用，
/// 不参与区块坐标计算、Tilemap 绘制或地图主题数据管理。这样未来接入正式的
/// 地图事件、剧情触发器或 UI 按钮时，可以直接删除或替换本组件，而不会污染地图核心。
/// </summary>
[DisallowMultipleComponent]
public class MapThemeDebugSwitcher : MonoBehaviour
{
    [Header("核心引用")]
    [Tooltip("负责实际执行地图主题切换的流式管理器。为空时会尝试获取同一物体上的组件。")]
    [SerializeField] private MapStreamManager mapStreamManager;

    [Tooltip("F 键切换时使用的草地主题。")]
    [SerializeField] private MapThemeDataSO grassTheme;

    [Tooltip("F 键切换时使用的机械地板主题。")]
    [SerializeField] private MapThemeDataSO mechanicalTheme;

    [Header("调试输入")]
    [Tooltip("按下该按键时，在草地和机械地板之间切换。原型阶段默认使用 F。")]
    [SerializeField] private KeyCode switchKey = KeyCode.F;

    [Tooltip("游戏暂停时不响应调试切换，避免升级面板期间误触发地图变化。")]
    [SerializeField] private bool ignoreWhenPaused = true;

    private void Awake()
    {
        if (mapStreamManager == null)
        {
            mapStreamManager = GetComponent<MapStreamManager>();
        }
    }

    private void Update()
    {
        if (!Input.GetKeyDown(switchKey))
        {
            return;
        }

        if (ignoreWhenPaused && Time.timeScale <= 0f)
        {
            return;
        }

        if (!ValidateReferences())
        {
            return;
        }

        MapThemeDataSO nextTheme = GetNextTheme();
        if (nextTheme == null)
        {
            Debug.LogError("MapThemeDebugSwitcher 没有可切换的下一个地图主题。", this);
            return;
        }

        if (!mapStreamManager.SwitchTheme(nextTheme))
        {
            Debug.LogError($"地图主题切换失败：{nextTheme.name}。", this);
            return;
        }

        Debug.Log($"地图主题已切换为：{nextTheme.ThemeId}。", this);
    }

    private bool ValidateReferences()
    {
        if (mapStreamManager == null)
        {
            Debug.LogError("MapThemeDebugSwitcher 缺少 MapStreamManager 引用。", this);
            return false;
        }

        if (grassTheme == null || !grassTheme.IsValid)
        {
            Debug.LogError("MapThemeDebugSwitcher 的草地主题引用无效。", this);
            return false;
        }

        if (mechanicalTheme == null || !mechanicalTheme.IsValid)
        {
            Debug.LogError("MapThemeDebugSwitcher 的机械地板主题引用无效。", this);
            return false;
        }

        return true;
    }

    private MapThemeDataSO GetNextTheme()
    {
        if (mapStreamManager.CurrentTheme == grassTheme)
        {
            return mechanicalTheme;
        }

        if (mapStreamManager.CurrentTheme == mechanicalTheme)
        {
            return grassTheme;
        }

        return grassTheme;
    }
}
```

#### After：删除结果

```text
文件已删除。
```

F 键职责迁移到：

```text
WorldLineCoordinator.Update()
    ↓
WorldLineCoordinator.SwitchWorldLine()
    ↓
WorldSlot.ApplyActiveState()
```

---

## 四、Unity 序列化文件 Before/After

### 1. `Assets/Prefab/Map/MapSegment.prefab`

#### Before：Prefab 层级

```text
MapSegment
├── Grid
├── Ground Tilemap
├── Ground TilemapRenderer
└── MapSegment
```

根节点只有 Ground Tilemap，MapSegment 只序列化：

```yaml
groundTilemap: {fileID: 100100004}
```

#### After：Prefab 层级

```text
MapSegment
├── Grid
├── Ground Tilemap
├── Ground TilemapRenderer
├── Rigidbody2D
├── MapSegment
└── Cover
    ├── Cover Tilemap
    ├── Cover TilemapRenderer
    └── TilemapCollider2D
```

MapSegment 组件引用变为：

```yaml
groundTilemap: {fileID: 100100004}
groundRenderer: {fileID: 100100005}
coverTilemap: {fileID: 100100010}
coverRenderer: {fileID: 100100011}
coverCollider: {fileID: 100100012}
```

新增 Rigidbody2D 配置：

```yaml
m_BodyType: 1
m_Simulated: 1
m_GravityScale: 0
m_CollisionDetection: 0
```

`m_BodyType: 1` 表示 Kinematic，用于支持区块对象在流式复用时移动。

---

### 2. `Assets/Scenes/MainLevel.unity`

#### Before：单世界结构

```text
MapStreamManager
└── MapStreamManager
    └── MapThemeDebugSwitcher
```

旧组件配置核心字段：

```yaml
mapSegmentPrefab: {fileID: 100100006, guid: a4f6c8e0b2d34d94d1f5a7c9e3b48602, type: 3}
initialTheme: {fileID: 11400000, guid: f7c9e1a3b5d65027a4c8e0f2b6d71935, type: 2}
target: {fileID: 1127848267}
activeRadiusInSegments: 1
```

#### After：双世界结构

```text
WorldLineCoordinator
├── MainWorldRuntime
│   ├── MapStreamManager
│   └── WorldEnemySimulation
└── SubWorldRuntime
    ├── MapStreamManager
    └── WorldEnemySimulation
```

协调器核心引用：

```yaml
mainWorld:
  worldLine: {fileID: 11400000, guid: d0f2a4c6e8b07491d7f9b1c3e5a86248, type: 2}
  mapStreamManager: {fileID: 1780000012}
  enemySimulation: {fileID: 1780000013}
subWorld:
  worldLine: {fileID: 11400000, guid: e1f3b5d7a9c18502e8a0c2d4f6b97359, type: 2}
  mapStreamManager: {fileID: 1780000022}
  enemySimulation: {fileID: 1780000023}
player: {fileID: 1127848267}
switchKey: 102
ignoreWhenPaused: 1
```

主世界地图管理器：

```yaml
mapSegmentPrefab: {fileID: 100100006, guid: a4f6c8e0b2d34d94d1f5a7c9e3b48602, type: 3}
initialWorldLine: {fileID: 11400000, guid: d0f2a4c6e8b07491d7f9b1c3e5a86248, type: 2}
target: {fileID: 1127848267}
activeRadiusInSegments: 1
```

副世界地图管理器：

```yaml
mapSegmentPrefab: {fileID: 100100006, guid: a4f6c8e0b2d34d94d1f5a7c9e3b48602, type: 3}
initialWorldLine: {fileID: 11400000, guid: e1f3b5d7a9c18502e8a0c2d4f6b97359, type: 2}
target: {fileID: 1127848267}
activeRadiusInSegments: 1
```

场景刷怪调整：

```yaml
GameObject: WaveManager
  m_IsActive: 0
```

`TestSpawner` 原本已经是停用状态，本次保持：

```yaml
GameObject: TestSpawner
  m_IsActive: 0
```

---

### 3. 世界线 ScriptableObject

#### `GrassWorldLine.asset`

```yaml
worldLineId: main
displayNameKey: world.main
groundTheme: {fileID: 11400000, guid: f7c9e1a3b5d65027a4c8e0f2b6d71935, type: 2}
coverSprite: {fileID: 21300000, guid: b8d0f2a4c6e85279b5d7f9a1c3e64026, type: 3}
coverCells:
- {x: 4, y: 4}
- {x: 5, y: 4}
- {x: 4, y: 5}
- {x: 10, y: 4}
- {x: 11, y: 4}
- {x: 10, y: 5}
- {x: 4, y: 11}
- {x: 5, y: 11}
- {x: 4, y: 12}
- {x: 10, y: 10}
- {x: 11, y: 10}
- {x: 10, y: 11}
safeRadiusInCells: 2
testEnemyPrefab: {fileID: 366242633661983014, guid: 5cbaf7a980e7e7b4891efaffd44e6ec0, type: 3}
testEnemySpawnOffsets:
- {x: 6, y: 0}
- {x: -6, y: 2}
- {x: 3, y: 6}
```

草地世界掩体模板：

```yaml
coverCells:
- {x: 4, y: 4}
- {x: 5, y: 4}
- {x: 4, y: 5}
- {x: 10, y: 4}
- {x: 11, y: 4}
- {x: 10, y: 5}
- {x: 4, y: 11}
- {x: 5, y: 11}
- {x: 4, y: 12}
- {x: 10, y: 10}
- {x: 11, y: 10}
- {x: 10, y: 11}
```

#### `MechanicalWorldLine.asset`

```yaml
worldLineId: sub
displayNameKey: world.sub
groundTheme: {fileID: 11400000, guid: a8d0f2b4c6e75138b5d9f1a3c7e82046, type: 2}
coverSprite: {fileID: 21300000, guid: c9e1f3b5d7a96380c6e8a0b2d4f75137, type: 3}
coverCells:
- {x: 2, y: 5}
- {x: 2, y: 6}
- {x: 2, y: 7}
- {x: 13, y: 4}
- {x: 12, y: 4}
- {x: 13, y: 5}
- {x: 5, y: 12}
- {x: 6, y: 12}
- {x: 7, y: 12}
- {x: 10, y: 8}
- {x: 11, y: 8}
- {x: 12, y: 8}
- {x: 8, y: 3}
- {x: 8, y: 4}
safeRadiusInCells: 2
testEnemyPrefab: {fileID: 366242633661983014, guid: 5cbaf7a980e7e7b4891efaffd44e6ec0, type: 3}
testEnemySpawnOffsets:
- {x: -5, y: -2}
- {x: 5, y: 3}
- {x: 0, y: -6}
```

机械世界掩体模板：

```yaml
coverCells:
- {x: 2, y: 5}
- {x: 2, y: 6}
- {x: 2, y: 7}
- {x: 13, y: 4}
- {x: 12, y: 4}
- {x: 13, y: 5}
- {x: 5, y: 12}
- {x: 6, y: 12}
- {x: 7, y: 12}
- {x: 10, y: 8}
- {x: 11, y: 8}
- {x: 12, y: 8}
- {x: 8, y: 3}
- {x: 8, y: 4}
```

---

## 五、资源导入配置

### 地面图集

- `GrassTiles.png`：`128x32`，包含 4 个 `32x32` Sprite。
- `MechanicalTiles.png`：`128x32`，包含 4 个 `32x32` Sprite。
- `spritePixelsToUnits: 32`。
- `filterMode: 0`，Point 过滤。
- 关闭 mipmap。
- 关闭纹理压缩。

### 掩体素材

- `GrassCover.png`：`32x32` 单 Sprite。
- `MechanicalCover.png`：`32x32` 单 Sprite。
- `spritePixelsToUnits: 32`。
- 掩体运行时 Tile 使用 `Tile.ColliderType.Grid`。

---

## 六、问题修复 Before/After

### 1. Sprite 多子资源 fileID 错误

**Before**：主题资产使用猜测的通用 Sprite fileID：

```yaml
groundSprites:
- {fileID: 21300000, guid: d5a7c9e1b3f44e05e2a6c8d0f4b59713, type: 3}
- {fileID: 21300008, guid: d5a7c9e1b3f44e05e2a6c8d0f4b59713, type: 3}
```

**表现**：Unity 主题数组中的 Sprite 解析为空，`MapStreamManager` 报告主题无地面素材。

**After**：改为 `.meta` 中真实的 `internalID`：

```yaml
groundSprites:
- {fileID: -1000000001, guid: d5a7c9e1b3f44e05e2a6c8d0f4b59713, type: 3}
- {fileID: -1000000002, guid: d5a7c9e1b3f44e05e2a6c8d0f4b59713, type: 3}
```

### 2. Prefab 子节点类型错误

**Before**：根 Transform 的子节点列表引用了 Cover GameObject：

```yaml
m_Children:
- {fileID: 100100008}
```

**After**：改为 Cover Transform：

```yaml
m_Children:
- {fileID: 100100009}
```

### 3. 单世界刷怪与 MVP 测试敌人重复

**Before**：`WaveManager` 和测试敌人生成逻辑同时存在，可能导致额外敌人持续生成。

**After**：MainLevel 禁用 WaveManager，仅保留每个世界 3 只 `WorldEnemySimulation` 测试敌人。

---

## 七、设计决策和未实现内容

- 主世界与副世界同时运行，不通过 `SetActive(false)` 停止副世界。
- 副世界地图和敌人持续更新，但 Renderer 和 Collider 关闭。
- 玩家属性、经验、武器和 Transform 在两个世界之间共享。
- 子弹暂时不与掩体交互。
- 副世界敌人暂不处理自身掩体路径碰撞。
- 暂不实现世界独立 WaveManager。
- 暂不实现世界运行时状态快照。

---

## 八、验证状态

- Unity 2022.3 脚本编译：✅ 通过。
- `WorldLineDataSO` 资源导入：✅ 通过。
- `MapSegment.prefab` 导入：✅ 通过。
- `MainLevel.unity` 序列化引用：✅ 静态检查通过。
- 自动 Play Mode 测试：⚠️ 因 Plastic 认证交互阻塞，未完成。
- 用户运行观察：✅ 已看到地图和掩体。
- 双世界敌人同步、F 切换后敌人位置保持：待手动回归。
