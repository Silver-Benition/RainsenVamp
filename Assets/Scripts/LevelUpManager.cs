using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class LevelUpManager : MonoBehaviour
{
    public static LevelUpManager Instance { get; private set; }

    [Header("UI 引用")]
    public GameObject levelUpPanel;             // 整个升级面板
    public Transform buttonContainer;           // 按钮容器（动态生成的父节点）
    public GameObject upgradeButtonPrefab;      // 升级按钮 Prefab：Assets/Prefabs/UI/UpgradeButton

    [Header("选项数量")]
    [Tooltip("升级面板每次展示的候选数量，默认3。后续做‘花金币重掷’时可改为动态值。")]
    public int upgradeChoiceCount = 3;

    [Header("数据池")]
    public List<UpgradeDataSO> allAvailableUpgrades; // 策划配置的所有可能出现的升级项

    // 运行时生成的按钮实例（关闭面板时销毁，下次重新生成）
    private readonly List<UpgradeUIItem> activeButtons = new List<UpgradeUIItem>();

    private Transform playerTransform;
    private readonly Dictionary<string, WeaponBase> ownedWeapons = new Dictionary<string, WeaponBase>();

    /// <summary>
    /// 建立升级管理器单例并确保升级面板初始隐藏。
    /// </summary>
    private void Awake()
    {
        if (Instance != null && Instance != this) Destroy(gameObject);
        else Instance = this;

        levelUpPanel.SetActive(false);
    }

    /// <summary>
    /// 缓存玩家并登记场景预置武器，使后续候选过滤能识别默认火球。
    /// </summary>
    private void Start()
    {
        // 缓存玩家引用，用于后续发放武器
        playerTransform = GameObject.FindWithTag("Player").transform;

        // 扫描玩家身上已有的武器（场景预置的默认武器），注册进 ownedWeapons
        // 这样升级系统才能感知到默认武器的存在，避免重复发放 / 正确处理升级等级
        RegisterDefaultWeapons();
    }

    /// <summary>
    /// 扫描 Player 子物体上已挂载的 WeaponBase，注册进 ownedWeapons 字典。
    /// 应在 Start() 里调用一次，处理角色自带默认武器的情况。
    /// </summary>
    private void RegisterDefaultWeapons()
    {
        if (playerTransform == null) return;

        WeaponBase[] existingWeapons = playerTransform.GetComponentsInChildren<WeaponBase>();
        foreach (var weapon in existingWeapons)
        {
            if (weapon == null || weapon.weaponData == null) continue;

            string weaponId = GetWeaponId(weapon.weaponData);
            if (string.IsNullOrEmpty(weaponId)) continue;

            // 只注册尚未记录的武器，避免重复注册
            if (!ownedWeapons.ContainsKey(weaponId))
            {
                ownedWeapons[weaponId] = weapon;
                Debug.Log($"[LevelUpManager] 注册默认武器: {weapon.weaponData.weaponNameKey} (ID: {weaponId}) Lv.{weapon.CurrentLevel}");
            }
        }
    }

    /// <summary>
    /// 触发升级面板：清除旧按钮 → 抽候选 → 动态生成 Prefab
    /// </summary>
    public void ShowLevelUpUI()
    {
        // 候选池：过滤掉已拥有且已满级的武器
        List<UpgradeDataSO> pool = BuildSelectableUpgradePool();

        // 保底处理：如果没有可选升级项，直接跳过面板，恢复游戏
        if (pool.Count == 0)
        {
            Debug.Log("[LevelUpManager] 候选池为空（所有武器已满级），跳过升级面板。");
            // 依然检查队列中是否有残余升级
            playerTransform.GetComponent<PlayerStats>().CheckLevelUpQueue();
            return;
        }

        if (GameFlowManager.Instance != null)
        {
            GameFlowManager.Instance.EnterLevelUpPause();
        }
        else
        {
            // 缺少统一流程管理器时保留旧行为，避免升级界面在测试场景中失去暂停能力。
            Time.timeScale = 0f;
        }
        levelUpPanel.SetActive(true);

        // 销毁上一次生成的按钮（避免残留旧候选）
        foreach (var btn in activeButtons)
        {
            if (btn != null) Destroy(btn.gameObject);
        }
        activeButtons.Clear();

        // 动态生成按钮 Prefab，数量取"期望数量"和"候选池数量"的较小值
        int count = Mathf.Min(upgradeChoiceCount, pool.Count);
        for (int i = 0; i < count; i++)
        {
            int randomIndex = Random.Range(0, pool.Count);
            UpgradeDataSO selectedUpgrade = pool[randomIndex];
            pool.RemoveAt(randomIndex); // 防止同一选项出现两次

            // 在 buttonContainer 下生成 Prefab
            GameObject btnObj = Instantiate(upgradeButtonPrefab, buttonContainer);
            if (btnObj.TryGetComponent<UpgradeUIItem>(out var uiItem))
            {
                uiItem.Setup(selectedUpgrade);
                activeButtons.Add(uiItem);
            }
        }
    }

    /// <summary>
    /// 玩家点击按钮后调用
    /// </summary>
    public void ApplyUpgrade(UpgradeDataSO selectedData)
    {
        // 1. 发放奖励
        if (selectedData.weaponToGrant != null)
        {
            GrantWeapon(selectedData);
        }

        // 2. 关闭面板、恢复时间
        levelUpPanel.SetActive(false);
        if (GameFlowManager.Instance != null)
        {
            GameFlowManager.Instance.ExitLevelUpPause();
        }
        else
        {
            Time.timeScale = 1f;
        }

        // 3. 通知 PlayerStats 检查是否还有排队的升级
        playerTransform.GetComponent<PlayerStats>().CheckLevelUpQueue();
    }

    /// <summary>
    /// 已持有武器时提升等级，否则创建对应运行类型的新武器子对象。
    /// </summary>
    private void GrantWeapon(UpgradeDataSO upgradeData)
    {
        if (upgradeData == null || upgradeData.weaponToGrant == null)
        {
            return;
        }

        GrantOrUpgradeWeapon(upgradeData.weaponToGrant);
    }

    /// <summary>
    /// 执行一次正式的武器授予：未持有时创建 Lv.1，已持有时提升一级，并返回最终运行时组件。
    /// </summary>
    /// <param name="weaponData">需要授予或升级的武器静态配置。</param>
    /// <returns>成功时返回玩家持有的武器组件；配置或玩家无效时返回 null。</returns>
    private WeaponBase GrantOrUpgradeWeapon(WeaponDataSO weaponData)
    {
        if (weaponData == null || playerTransform == null)
        {
            Debug.LogWarning("[LevelUpManager] 武器授予失败：武器配置或玩家引用无效。");
            return null;
        }

        string weaponId = GetWeaponId(weaponData);

        // 已拥有 → 升级等级
        if (ownedWeapons.TryGetValue(weaponId, out var existingWeapon))
        {
            if (existingWeapon != null && existingWeapon.TryLevelUp())
            {
                Debug.Log($"武器升级成功: {weaponData.weaponNameKey} Lv.{existingWeapon.CurrentLevel}/{existingWeapon.MaxLevel}");
            }
            else
            {
                Debug.Log($"武器已满级，跳过: {weaponData.weaponNameKey}");
            }
            return existingWeapon;
        }

        // 未拥有 → 动态挂载武器脚本
        GameObject newWeaponObj = new GameObject($"Weapon_{weaponData.weaponID}");
        newWeaponObj.transform.SetParent(playerTransform);
        newWeaponObj.transform.localPosition = Vector3.zero;

        WeaponBase weaponBase = CreateWeaponRuntime(newWeaponObj, weaponData.runtimeType);
        weaponBase.weaponData = weaponData;

        ownedWeapons[weaponId] = weaponBase;
        Debug.Log($"获得新武器: {weaponData.weaponNameKey} Lv.{weaponBase.CurrentLevel}/{weaponBase.MaxLevel}");
        return weaponBase;
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    /// <summary>
    /// 调试环境下确保玩家持有指定武器并至少达到目标等级；只允许升级，不执行降级或删除。
    /// </summary>
    /// <param name="weaponData">需要测试的武器静态配置。</param>
    /// <param name="targetLevel">期望达到的等级，会限制在该武器的有效等级范围内。</param>
    /// <returns>成功时返回武器运行时组件；找不到玩家或配置无效时返回 null。</returns>
    public WeaponBase DebugEnsureWeaponLevel(WeaponDataSO weaponData, int targetLevel)
    {
        if (weaponData == null || !EnsureDebugPlayerReference())
        {
            return null;
        }

        int safeTargetLevel = Mathf.Clamp(targetLevel, 1, weaponData.MaxLevel);
        WeaponBase weapon = GetOwnedWeapon(weaponData);

        if (weapon == null)
        {
            weapon = GrantOrUpgradeWeapon(weaponData);
        }

        while (weapon != null && weapon.CurrentLevel < safeTargetLevel)
        {
            if (!weapon.TryLevelUp())
            {
                break;
            }
        }

        if (weapon != null)
        {
            Debug.Log($"[WeaponDebug] {weaponData.weaponNameKey} 已就绪：Lv.{weapon.CurrentLevel}/{weapon.MaxLevel}");
        }

        return weapon;
    }

    /// <summary>
    /// 调试面板早于 Start 调用时补齐玩家引用，并登记场景中的默认武器。
    /// </summary>
    /// <returns>玩家引用有效时返回 true，否则记录警告并返回 false。</returns>
    private bool EnsureDebugPlayerReference()
    {
        if (playerTransform != null)
        {
            return true;
        }

        GameObject playerObject = GameObject.FindWithTag("Player");
        if (playerObject == null)
        {
            Debug.LogWarning("[WeaponDebug] 找不到 Player，无法授予测试武器。");
            return false;
        }

        playerTransform = playerObject.transform;
        RegisterDefaultWeapons();
        return true;
    }
#endif

    /// <summary>
    /// 根据武器数据中的稳定运行类型挂载对应组件，避免升级卡与武器行为配置不一致。
    /// </summary>
    /// <param name="weaponObject">本次动态创建的武器宿主对象。</param>
    /// <param name="runtimeType">武器数据声明的运行时类型。</param>
    /// <returns>已经挂载到宿主对象上的武器运行时组件。</returns>
    private WeaponBase CreateWeaponRuntime(GameObject weaponObject, WeaponRuntimeType runtimeType)
    {
        switch (runtimeType)
        {
            case WeaponRuntimeType.Aura:
                return weaponObject.AddComponent<AuraWeapon>();
            case WeaponRuntimeType.Orbiting:
                return weaponObject.AddComponent<OrbitWeapon>();
            case WeaponRuntimeType.Lobbed:
                return weaponObject.AddComponent<LobbedWeapon>();
            case WeaponRuntimeType.Melee:
                return weaponObject.AddComponent<MeleeWeapon>();
            default:
                return weaponObject.AddComponent<WeaponBase>();
        }
    }

    /// <summary>
    /// 构建未获得或尚未满级的升级候选副本，不修改配置资产中的原始列表。
    /// </summary>
    private List<UpgradeDataSO> BuildSelectableUpgradePool()
    {
        List<UpgradeDataSO> pool = new List<UpgradeDataSO>();

        for (int i = 0; i < allAvailableUpgrades.Count; i++)
        {
            UpgradeDataSO upgrade = allAvailableUpgrades[i];
            if (upgrade == null) continue;

            // 非武器升级直接放行
            if (upgrade.weaponToGrant == null)
            {
                pool.Add(upgrade);
                continue;
            }

            string weaponId = GetWeaponId(upgrade.weaponToGrant);

            // 未拥有 → 放行（首次解锁）
            if (!ownedWeapons.TryGetValue(weaponId, out var ownedWeapon))
            {
                pool.Add(upgrade);
                continue;
            }

            // 已拥有但未满级 → 放行（升级）
            if (ownedWeapon != null && !ownedWeapon.IsMaxLevel)
            {
                pool.Add(upgrade);
            }
            // 已满级 → 过滤掉，不出现在候选里
        }

        return pool;
    }

    /// <summary>
    /// 读取武器稳定 ID；旧资产缺少 ID 时暂以资产名作为安全回退。
    /// </summary>
    private string GetWeaponId(WeaponDataSO weaponData)
    {
        if (weaponData == null) return string.Empty;
        if (!string.IsNullOrEmpty(weaponData.weaponID)) return weaponData.weaponID;
        return weaponData.name;
    }

    /// <summary>
    /// 查询玩家是否已拥有某把武器，供 UpgradeUIItem 显示当前等级。
    /// 未拥有时返回 null。
    /// </summary>
    public WeaponBase GetOwnedWeapon(WeaponDataSO weaponData)
    {
        if (weaponData == null) return null;
        string weaponId = GetWeaponId(weaponData);
        ownedWeapons.TryGetValue(weaponId, out var weapon);
        return weapon;
    }
}
