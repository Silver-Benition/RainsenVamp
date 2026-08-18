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
    /// <param name="worldLine">区块所属的世界线。</param>
    /// <param name="segmentCoordinate">无限坐标系中的区块坐标。</param>
    /// <param name="worldPosition">区块左下角世界坐标。</param>
    /// <param name="protectedWorldPosition">玩家位置，用于生成安全区。</param>
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
