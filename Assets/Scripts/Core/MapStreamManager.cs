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
