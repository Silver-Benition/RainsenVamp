using System;
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
    private readonly List<UpgradeDataSO> _currentCandidates = new List<UpgradeDataSO>();

    private Transform playerTransform;
    private PlayerStats _playerStats;
    private AbilityManager _abilityManager;
    private RunState _runState;
    private LevelUpActionBarUI _actionBar;
    private readonly IRandomSource _randomSource = new UnityRandomSource();
    private bool _banishMode;
    private readonly Dictionary<string, WeaponBase> ownedWeapons = new Dictionary<string, WeaponBase>();
    private readonly List<WeaponBase> _ownedWeaponOrder =
        new List<WeaponBase>(PlayerLoadoutRules.MaxWeaponCount);

    /// <summary>玩家持有武器的种类或等级变化时触发，供 HUD 等只读表现层刷新。</summary>
    public event Action OwnedWeaponsChanged;

    /// <summary>按首次获得顺序排列的只读武器列表。</summary>
    public IReadOnlyList<WeaponBase> OwnedWeapons => _ownedWeaponOrder;

    /// <summary>玩家当前持有的不同武器种类数。</summary>
    public int OwnedWeaponCount => _ownedWeaponOrder.Count;

    /// <summary>当前面板正在展示的只读候选快照。</summary>
    public IReadOnlyList<UpgradeDataSO> CurrentCandidates => _currentCandidates;

    /// <summary>当前是否等待玩家点击一个候选执行放逐。</summary>
    public bool IsBanishMode => _banishMode;

    /// <summary>
    /// 建立升级管理器单例并确保升级面板初始隐藏。
    /// </summary>
    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        if (levelUpPanel != null)
        {
            levelUpPanel.SetActive(false);
        }
    }

    /// <summary>
    /// 缓存玩家并登记场景预置武器，使后续候选过滤能识别默认火球。
    /// </summary>
    private void Start()
    {
        if (!ResolvePlayerReferences())
        {
            Debug.LogError("[LevelUpManager] 找不到 Player，无法登记或发放武器。", this);
            return;
        }

        // 扫描玩家身上已有的武器（场景预置的默认武器），注册进 ownedWeapons
        // 这样升级系统才能感知到默认武器的存在，避免重复发放 / 正确处理升级等级
        RegisterDefaultWeapons();
        EnsureCharacterStartingWeapon();
    }

    /// <summary>销毁时释放单例引用，避免场景重载后其他系统取得失效管理器。</summary>
    private void OnDestroy()
    {
        if (_runState != null)
        {
            _runState.StateChanged -= RefreshActionBar;
        }

        if (Instance == this)
        {
            Instance = null;
        }
    }

    /// <summary>
    /// 扫描 Player 子物体上已挂载的 WeaponBase，注册进 ownedWeapons 字典。
    /// 应在 Start() 里调用一次，处理角色自带默认武器的情况。
    /// </summary>
    private void RegisterDefaultWeapons()
    {
        if (playerTransform == null) return;

        bool inventoryChanged = false;
        WeaponBase[] existingWeapons = playerTransform.GetComponentsInChildren<WeaponBase>();
        foreach (var weapon in existingWeapons)
        {
            if (weapon == null || weapon.weaponData == null) continue;

            string weaponId = GetWeaponId(weapon.weaponData);
            if (string.IsNullOrEmpty(weaponId)) continue;

            // 只注册尚未记录的武器，避免重复注册
            if (!ownedWeapons.ContainsKey(weaponId))
            {
                if (OwnedWeaponCount >= PlayerLoadoutRules.MaxWeaponCount)
                {
                    weapon.enabled = false;
                    Debug.LogError(
                        $"[LevelUpManager] 场景预置武器超过 {PlayerLoadoutRules.MaxWeaponCount} 种上限，" +
                        $"已禁用多余武器：{weapon.weaponData.weaponNameKey}",
                        weapon);
                    continue;
                }

                ownedWeapons[weaponId] = weapon;
                _ownedWeaponOrder.Add(weapon);
                inventoryChanged = true;
                AccountProgressService.Current.DiscoverWeapon(weaponId);
                Debug.Log($"[LevelUpManager] 注册默认武器: {weapon.weaponData.weaponNameKey} (ID: {weaponId}) Lv.{weapon.CurrentLevel}");
            }
        }

        if (inventoryChanged)
        {
            NotifyOwnedWeaponsChanged();
        }
    }

    /// <summary>
    /// 触发升级面板：清除旧按钮 → 抽候选 → 动态生成 Prefab
    /// </summary>
    public void ShowLevelUpUI()
    {
        if (!ResolvePlayerReferences())
        {
            Debug.LogError("[LevelUpManager] 找不到玩家属性，无法展示升级候选。", this);
            return;
        }

        if (!TryBuildCurrentCandidates())
        {
            Debug.Log("[LevelUpManager] 候选池为空，跳过本次升级选择。", this);
            ContinueLevelQueue();
            return;
        }

        if (GameFlowManager.Instance != null)
        {
            GameFlowManager.Instance.EnterLevelUpPause();
        }
        else
        {
            Time.timeScale = 0f;
        }

        _banishMode = false;
        levelUpPanel.SetActive(true);
        RebuildCandidateButtons();
        EnsureActionBar();
        RefreshActionBar();
    }

    /// <summary>
    /// 玩家点击按钮后调用
    /// </summary>
    public void ApplyUpgrade(UpgradeDataSO selectedData)
    {
        HandleCandidateSelected(selectedData);
    }

    /// <summary>
    /// 处理候选卡点击：普通模式授予奖励，放逐模式则消费次数并刷新当前候选。
    /// </summary>
    public void HandleCandidateSelected(UpgradeDataSO selectedData)
    {
        if (selectedData == null || !_currentCandidates.Contains(selectedData))
        {
            return;
        }

        if (_banishMode)
        {
            TryBanishCandidate(selectedData);
            return;
        }

        // 候选池已保证二选一奖励；这里仍使用互斥分支，避免异常资产在一次点击中发放两份内容。
        if (selectedData.weaponToGrant != null)
        {
            GrantWeapon(selectedData);
        }
        else if (selectedData.abilityToGrant != null)
        {
            GrantAbility(selectedData);
        }

        CompleteCurrentChoice();
    }

    /// <summary>消耗一次重掷并用同一合法池重新生成当前候选。</summary>
    public void RerollCurrentChoices()
    {
        if (_runState == null || levelUpPanel == null || !levelUpPanel.activeSelf ||
            !_runState.TryConsumeReroll())
        {
            return;
        }

        _banishMode = false;
        RefreshCandidatesOrComplete();
    }

    /// <summary>消耗一次跳过，放弃本次奖励并继续处理升级队列。</summary>
    public void SkipCurrentChoice()
    {
        if (_runState == null || levelUpPanel == null || !levelUpPanel.activeSelf ||
            !_runState.TryConsumeSkip())
        {
            return;
        }

        CompleteCurrentChoice();
    }

    /// <summary>在存在放逐次数时切换候选点击的放逐模式。</summary>
    public void ToggleBanishMode()
    {
        if (_runState == null || _runState.RemainingBanishes <= 0 ||
            levelUpPanel == null || !levelUpPanel.activeSelf)
        {
            _banishMode = false;
        }
        else
        {
            _banishMode = !_banishMode;
        }

        RefreshActionBar();
    }

    /// <summary>
    /// 为宝箱即时抽取并授予一个合法升级，不占用玩家升级队列。
    /// </summary>
    /// <returns>成功授予的升级数据；候选为空或奖励无效时返回 null。</returns>
    public UpgradeDataSO GrantRandomChestReward()
    {
        if (!ResolvePlayerReferences())
        {
            return null;
        }

        List<UpgradeDataSO> selectablePool = BuildSelectableUpgradePool();
        var weaponPool = new List<UpgradeDataSO>(selectablePool.Count);
        for (int index = 0; index < selectablePool.Count; index++)
        {
            UpgradeDataSO candidate = selectablePool[index];
            if (candidate != null && candidate.weaponToGrant != null)
            {
                weaponPool.Add(candidate);
            }
        }

        List<UpgradeDataSO> reward = UpgradeCandidateResolver.SelectWeightedWithoutReplacement(
            weaponPool,
            1,
            _playerStats != null ? _playerStats.Luck : 1f,
            _randomSource);
        if (reward.Count == 0 || reward[0] == null || reward[0].weaponToGrant == null)
        {
            return null;
        }

        GrantWeapon(reward[0]);
        return reward[0];
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

    /// <summary>通过玩家 AbilityManager 获得或升级正式能力。</summary>
    private void GrantAbility(UpgradeDataSO upgradeData)
    {
        if (upgradeData == null || upgradeData.abilityToGrant == null || _abilityManager == null)
        {
            return;
        }

        _abilityManager.GrantOrUpgrade(upgradeData.abilityToGrant);
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
        if (string.IsNullOrEmpty(weaponId))
        {
            Debug.LogWarning("[LevelUpManager] 武器授予失败：武器稳定 ID 为空。");
            return null;
        }

        // 已拥有 → 升级等级
        if (ownedWeapons.TryGetValue(weaponId, out var existingWeapon))
        {
            AccountProgressService.Current.DiscoverWeapon(weaponId);
            if (existingWeapon != null && existingWeapon.TryLevelUp())
            {
                Debug.Log($"武器升级成功: {weaponData.weaponNameKey} Lv.{existingWeapon.CurrentLevel}/{existingWeapon.MaxLevel}");
                NotifyOwnedWeaponsChanged();
            }
            else
            {
                Debug.Log($"武器已满级，跳过: {weaponData.weaponNameKey}");
            }
            return existingWeapon;
        }

        if (OwnedWeaponCount >= PlayerLoadoutRules.MaxWeaponCount)
        {
            Debug.LogWarning(
                $"[LevelUpManager] 已达到 {PlayerLoadoutRules.MaxWeaponCount} 种武器上限，" +
                $"无法获得新武器：{weaponData.weaponNameKey}");
            return null;
        }

        WeaponBase weaponBase = CreateNewWeapon(weaponData, weaponId);
        if (weaponBase == null)
        {
            return null;
        }

        NotifyOwnedWeaponsChanged();
        Debug.Log($"获得新武器: {weaponData.weaponNameKey} Lv.{weaponBase.CurrentLevel}/{weaponBase.MaxLevel}");
        return weaponBase;
    }

    /// <summary>
    /// 确保当前角色的起始武器以 Lv.1 存在。
    /// 已登记同 ID 武器时不会调用升级，防止兼容场景或测试夹具从 Lv.1 误升到 Lv.2。
    /// </summary>
    private void EnsureCharacterStartingWeapon()
    {
        WeaponDataSO startingWeapon = _playerStats != null && _playerStats.CharacterData != null
            ? _playerStats.CharacterData.startingWeapon
            : null;
        if (startingWeapon == null || playerTransform == null)
        {
            return;
        }

        string weaponId = GetWeaponId(startingWeapon);
        if (string.IsNullOrWhiteSpace(weaponId))
        {
            Debug.LogError("[LevelUpManager] 当前角色的起始武器缺少稳定 ID。", startingWeapon);
            return;
        }

        if (ownedWeapons.ContainsKey(weaponId))
        {
            AccountProgressService.Current.DiscoverWeapon(weaponId);
            return;
        }

        if (OwnedWeaponCount >= PlayerLoadoutRules.MaxWeaponCount)
        {
            Debug.LogError(
                $"[LevelUpManager] 武器栏已满，无法建立角色起始武器：{startingWeapon.weaponNameKey}",
                this);
            return;
        }

        WeaponBase created = CreateNewWeapon(startingWeapon, weaponId);
        if (created != null)
        {
            NotifyOwnedWeaponsChanged();
            Debug.Log($"[LevelUpManager] 建立角色起始武器：{startingWeapon.weaponNameKey} Lv.1");
        }
    }

    /// <summary>动态创建一把 Lv.1 武器并登记稳定 ID；调用方负责容量与重复检查。</summary>
    private WeaponBase CreateNewWeapon(WeaponDataSO weaponData, string weaponId)
    {
        if (weaponData == null || playerTransform == null || string.IsNullOrWhiteSpace(weaponId))
        {
            return null;
        }

        GameObject newWeaponObject = new GameObject($"Weapon_{weaponId}");
        newWeaponObject.transform.SetParent(playerTransform);
        newWeaponObject.transform.localPosition = Vector3.zero;

        WeaponBase weaponBase = CreateWeaponRuntime(newWeaponObject, weaponData.runtimeType);
        weaponBase.weaponData = weaponData;
        ownedWeapons[weaponId] = weaponBase;
        _ownedWeaponOrder.Add(weaponBase);
        AccountProgressService.Current.DiscoverWeapon(weaponId);
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
        if (allAvailableUpgrades == null)
        {
            return pool;
        }

        for (int i = 0; i < allAvailableUpgrades.Count; i++)
        {
            UpgradeDataSO upgrade = allAvailableUpgrades[i];
            if (upgrade == null || !upgrade.HasExactlyOneReward()) continue;

            if (AccountProgressService.Current.IsUpgradeSealed(upgrade.GetStableId()))
            {
                continue;
            }

            if (_runState != null && _runState.IsBanished(upgrade.GetStableId()))
            {
                continue;
            }

            if (upgrade.abilityToGrant != null)
            {
                if (_abilityManager != null && _abilityManager.CanAcquireAbility(upgrade.abilityToGrant))
                {
                    pool.Add(upgrade);
                }
                continue;
            }

            string weaponId = GetWeaponId(upgrade.weaponToGrant);

            // 未拥有 → 放行（首次解锁）
            if (!ownedWeapons.TryGetValue(weaponId, out var ownedWeapon))
            {
                if (OwnedWeaponCount < PlayerLoadoutRules.MaxWeaponCount)
                {
                    pool.Add(upgrade);
                }
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

    /// <summary>查询玩家是否已拥有指定正式能力；能力管理器缺失时返回 null。</summary>
    public OwnedAbilityState GetOwnedAbility(AbilityDataSO abilityData)
    {
        return _abilityManager != null ? _abilityManager.GetOwnedAbility(abilityData) : null;
    }

    /// <summary>
    /// 判断指定武器当前是否可以获得或升级。
    /// 已持有武器不受种类上限影响；新武器仅在存在空槽时允许。
    /// </summary>
    /// <param name="weaponData">需要检查的武器静态数据。</param>
    /// <returns>可以获得或继续升级时返回 true。</returns>
    public bool CanAcquireWeapon(WeaponDataSO weaponData)
    {
        if (weaponData == null)
        {
            return false;
        }

        string weaponId = GetWeaponId(weaponData);
        if (string.IsNullOrEmpty(weaponId))
        {
            return false;
        }

        if (ownedWeapons.TryGetValue(weaponId, out var ownedWeapon) && ownedWeapon != null)
        {
            return true;
        }

        return OwnedWeaponCount < PlayerLoadoutRules.MaxWeaponCount;
    }

    /// <summary>集中发布武器清单变化，避免表现层轮询运行时组件。</summary>
    private void NotifyOwnedWeaponsChanged()
    {
        OwnedWeaponsChanged?.Invoke();
    }

    /// <summary>解析玩家、属性与局内状态，并建立低频状态订阅。</summary>
    private bool ResolvePlayerReferences()
    {
        if (playerTransform == null)
        {
            GameObject playerObject = GameObject.FindWithTag("Player");
            if (playerObject != null)
            {
                playerTransform = playerObject.transform;
            }
        }

        if (playerTransform == null)
        {
            return false;
        }

        if (_playerStats == null)
        {
            _playerStats = playerTransform.GetComponent<PlayerStats>();
        }

        if (_abilityManager == null)
        {
            _abilityManager = playerTransform.GetComponent<AbilityManager>();
        }

        RunState resolvedState = _playerStats != null
            ? RunState.GetOrCreate(_playerStats)
            : null;
        if (!ReferenceEquals(_runState, resolvedState))
        {
            if (_runState != null)
            {
                _runState.StateChanged -= RefreshActionBar;
            }

            _runState = resolvedState;
            if (_runState != null)
            {
                _runState.StateChanged += RefreshActionBar;
            }
        }

        // 轻量测试或未来非标准玩家可以只登记武器；Luck 使用中性值，局内次数按钮保持禁用。
        return playerTransform != null;
    }

    /// <summary>生成当前候选快照，返回是否至少存在一个可用候选。</summary>
    private bool TryBuildCurrentCandidates()
    {
        List<UpgradeDataSO> pool = BuildSelectableUpgradePool();
        List<UpgradeDataSO> selected = UpgradeCandidateResolver.SelectWeightedWithoutReplacement(
            pool,
            upgradeChoiceCount,
            _playerStats != null ? _playerStats.Luck : 1f,
            _randomSource);

        _currentCandidates.Clear();
        _currentCandidates.AddRange(selected);
        for (int index = 0; index < _currentCandidates.Count; index++)
        {
            UpgradeDataSO candidate = _currentCandidates[index];
            if (candidate != null)
            {
                AccountProgressService.Current.DiscoverUpgrade(candidate.GetStableId());
            }
        }
        return _currentCandidates.Count > 0;
    }

    /// <summary>清理旧卡片并按当前候选快照生成新按钮。</summary>
    private void RebuildCandidateButtons()
    {
        ClearCandidateButtons();
        if (upgradeButtonPrefab == null || buttonContainer == null)
        {
            Debug.LogError("[LevelUpManager] 升级按钮 Prefab 或容器未配置。", this);
            return;
        }

        for (int index = 0; index < _currentCandidates.Count; index++)
        {
            GameObject buttonObject = Instantiate(upgradeButtonPrefab, buttonContainer);
            if (buttonObject.TryGetComponent(out UpgradeUIItem uiItem))
            {
                uiItem.Setup(_currentCandidates[index]);
                activeButtons.Add(uiItem);
            }
        }
    }

    /// <summary>销毁上一次面板创建的低频候选按钮。</summary>
    private void ClearCandidateButtons()
    {
        for (int index = 0; index < activeButtons.Count; index++)
        {
            if (activeButtons[index] != null)
            {
                Destroy(activeButtons[index].gameObject);
            }
        }

        activeButtons.Clear();
    }

    /// <summary>创建一次运行时操作栏并绑定重掷、跳过和放逐行为。</summary>
    private void EnsureActionBar()
    {
        if (_actionBar != null || levelUpPanel == null)
        {
            return;
        }

        _actionBar = LevelUpActionBarUI.Create(levelUpPanel.transform);
        _actionBar.Bind(RerollCurrentChoices, SkipCurrentChoice, ToggleBanishMode);
    }

    /// <summary>把当前剩余次数与放逐模式同步到操作栏。</summary>
    private void RefreshActionBar()
    {
        if (_actionBar == null)
        {
            return;
        }

        _actionBar.Refresh(
            _runState != null ? _runState.RemainingRerolls : 0,
            _runState != null ? _runState.RemainingSkips : 0,
            _runState != null ? _runState.RemainingBanishes : 0,
            _banishMode);
    }

    /// <summary>消费放逐次数并记录稳定 ID；放逐本身占用并结束当前升级机会。</summary>
    private void TryBanishCandidate(UpgradeDataSO selectedData)
    {
        if (_runState == null || selectedData == null ||
            _runState.IsBanished(selectedData.GetStableId()) ||
            !_runState.TryConsumeBanish())
        {
            _banishMode = false;
            RefreshActionBar();
            return;
        }

        _runState.BanishUpgrade(selectedData.GetStableId());
        _banishMode = false;
        CompleteCurrentChoice();
    }

    /// <summary>重建候选；重掷后候选池耗尽时安全结束当前升级机会。</summary>
    private void RefreshCandidatesOrComplete()
    {
        if (!TryBuildCurrentCandidates())
        {
            CompleteCurrentChoice();
            return;
        }

        RebuildCandidateButtons();
        RefreshActionBar();
    }

    /// <summary>关闭当前升级面板、释放暂停并继续处理剩余升级队列。</summary>
    private void CompleteCurrentChoice()
    {
        _banishMode = false;
        _currentCandidates.Clear();
        ClearCandidateButtons();
        if (levelUpPanel != null)
        {
            levelUpPanel.SetActive(false);
        }

        if (GameFlowManager.Instance != null)
        {
            GameFlowManager.Instance.ExitLevelUpPause();
        }
        else
        {
            Time.timeScale = 1f;
        }

        ContinueLevelQueue();
    }

    /// <summary>通知玩家属性继续兑现可能排队的升级机会。</summary>
    private void ContinueLevelQueue()
    {
        if (_playerStats != null)
        {
            _playerStats.CheckLevelUpQueue();
        }
    }
}
