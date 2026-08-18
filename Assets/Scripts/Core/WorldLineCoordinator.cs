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
        public WorldWaveManager worldWaveManager;

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
    public WorldWaveManager MainWorldWaveManager => mainWorld != null ? mainWorld.worldWaveManager : null;
    public WorldWaveManager SubWorldWaveManager => subWorld != null ? subWorld.worldWaveManager : null;

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

        if (mainWorld == null || mainWorld.worldLine == null || mainWorld.mapStreamManager == null || mainWorld.enemySimulation == null || mainWorld.worldWaveManager == null)
        {
            Debug.LogError("WorldLineCoordinator 的主世界上下文配置不完整。", this);
            valid = false;
        }

        if (subWorld == null || subWorld.worldLine == null || subWorld.mapStreamManager == null || subWorld.enemySimulation == null || subWorld.worldWaveManager == null)
        {
            Debug.LogError("WorldLineCoordinator 的副世界上下文配置不完整。", this);
            valid = false;
        }

        return valid;
    }
}
