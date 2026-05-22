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

    private void Awake()
    {
        if (Instance != null && Instance != this) Destroy(gameObject);
        else Instance = this;

        levelUpPanel.SetActive(false);
    }

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

        Time.timeScale = 0f; // 暂停时间，怪物和子弹全部冻结
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
        Time.timeScale = 1f;

        // 3. 通知 PlayerStats 检查是否还有排队的升级
        playerTransform.GetComponent<PlayerStats>().CheckLevelUpQueue();
    }

    private void GrantWeapon(UpgradeDataSO upgradeData)
    {
        WeaponDataSO weaponData = upgradeData.weaponToGrant;
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
            return;
        }

        // 未拥有 → 动态挂载武器脚本
        GameObject newWeaponObj = new GameObject($"Weapon_{weaponData.weaponID}");
        newWeaponObj.transform.SetParent(playerTransform);
        newWeaponObj.transform.localPosition = Vector3.zero;

        WeaponBase weaponBase = upgradeData.runtimeType switch
        {
            WeaponRuntimeType.Aura => newWeaponObj.AddComponent<AuraWeapon>(),
            _ => newWeaponObj.AddComponent<WeaponBase>()
        };
        weaponBase.weaponData = weaponData;

        ownedWeapons[weaponId] = weaponBase;
        Debug.Log($"获得新武器: {weaponData.weaponNameKey} Lv.{weaponBase.CurrentLevel}/{weaponBase.MaxLevel}");
    }

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
