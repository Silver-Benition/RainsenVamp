using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 管理单局游戏的暂停原因、玩家死亡和重新开始流程。
/// 所有会冻结游戏时间的系统都通过此组件协调，避免某个界面错误解除另一个界面的暂停。
/// </summary>
[DisallowMultipleComponent]
public sealed class GameFlowManager : MonoBehaviour
{
    [Flags]
    private enum PauseReason
    {
        None = 0,
        LevelUp = 1 << 0,
        Manual = 1 << 1,
        GameOver = 1 << 2
    }

    public static GameFlowManager Instance { get; private set; }

    [Header("玩家引用")]
    [SerializeField] private PlayerHealth playerHealth;
    [SerializeField] private PlayerController playerController;
    [SerializeField] private Rigidbody2D playerRigidbody;

    [Header("界面引用")]
    [SerializeField] private GameObject levelUpPanel;
    [SerializeField] private GameObject pausePanel;
    [SerializeField] private GameObject gameOverPanel;
    [SerializeField] private Button resumeButton;
    [SerializeField] private Button pauseRestartButton;
    [SerializeField] private Button pauseMainMenuButton;
    [SerializeField] private Button gameOverRestartButton;

    [Header("输入配置")]
    [SerializeField] private KeyCode pauseKey = KeyCode.Escape;

    [Header("场景配置")]
    [SerializeField] private string mainMenuSceneName = "MainMenu";

    private PauseReason _pauseReasons;
    private bool _playerDeathSubscribed;
    private bool _isLoadingMainMenu;

    /// <summary>手动暂停状态变化时触发；升级选择与游戏结束不会被误报为手动暂停。</summary>
    public event Action<bool> ManualPauseChanged;

    /// <summary>游戏是否正处于任意暂停状态。</summary>
    public bool IsPaused => _pauseReasons != PauseReason.None;

    /// <summary>玩家是否通过暂停菜单进入了手动暂停。</summary>
    public bool IsManuallyPaused => HasPauseReason(PauseReason.Manual);

    /// <summary>本局是否已经进入不可恢复的游戏结束状态。</summary>
    public bool IsGameOver => HasPauseReason(PauseReason.GameOver);

    /// <summary>是否已经提交返回主菜单的异步加载请求。</summary>
    public bool IsLoadingMainMenu => _isLoadingMainMenu;

    /// <summary>建立单例、初始化界面和按钮，并确保新一局从正常时间流速开始。</summary>
    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        _pauseReasons = PauseReason.None;
        Time.timeScale = 1f;

        ResolvePlayerReferences();
        SetPanelActive(levelUpPanel, false);
        SetPanelActive(pausePanel, false);
        SetPanelActive(gameOverPanel, false);
        BindButtons();
    }

    /// <summary>组件启用时订阅玩家死亡事件。</summary>
    private void OnEnable()
    {
        SubscribePlayerDeath();
    }

    /// <summary>监听暂停按键；升级选择和游戏结束期间不允许切换手动暂停。</summary>
    private void Update()
    {
        if (_isLoadingMainMenu ||
            !Input.GetKeyDown(pauseKey) ||
            IsGameOver ||
            HasPauseReason(PauseReason.LevelUp))
        {
            return;
        }

        if (HasPauseReason(PauseReason.Manual))
        {
            ResumeGame();
        }
        else
        {
            PauseGame();
        }
    }

    /// <summary>组件停用时取消玩家死亡事件订阅。</summary>
    private void OnDisable()
    {
        UnsubscribePlayerDeath();
    }

    /// <summary>销毁时解除按钮监听并释放单例引用。</summary>
    private void OnDestroy()
    {
        UnbindButtons();

        if (Instance == this)
        {
            Instance = null;
        }
    }

    /// <summary>进入手动暂停并显示暂停面板。</summary>
    public void PauseGame()
    {
        if (IsGameOver || HasPauseReason(PauseReason.LevelUp) || IsManuallyPaused)
        {
            return;
        }

        AddPauseReason(PauseReason.Manual);
        SetPanelActive(pausePanel, true);
        NotifyManualPauseChanged(true);
    }

    /// <summary>解除手动暂停；其他暂停原因仍然存在时不会恢复游戏时间。</summary>
    public void ResumeGame()
    {
        if (!HasPauseReason(PauseReason.Manual) || IsGameOver)
        {
            return;
        }

        RemovePauseReason(PauseReason.Manual);
        SetPanelActive(pausePanel, false);
        NotifyManualPauseChanged(false);
    }

    /// <summary>由升级系统请求暂停，保留独立原因以避免被手动恢复覆盖。</summary>
    public void EnterLevelUpPause()
    {
        if (IsGameOver)
        {
            return;
        }

        AddPauseReason(PauseReason.LevelUp);
    }

    /// <summary>由升级系统释放暂停；仍有其他暂停原因时继续保持冻结。</summary>
    public void ExitLevelUpPause()
    {
        RemovePauseReason(PauseReason.LevelUp);
    }

    /// <summary>恢复正常时间流速并重新加载当前关卡。</summary>
    public void RestartGame()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    /// <summary>
    /// 从暂停菜单返回主界面；提交加载后锁定暂停菜单按钮，避免重复创建场景切换请求。
    /// </summary>
    public void ReturnToMainMenu()
    {
        if (_isLoadingMainMenu)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(mainMenuSceneName) ||
            !Application.CanStreamedLevelBeLoaded(mainMenuSceneName))
        {
            Debug.LogError(
                $"无法加载主菜单场景“{mainMenuSceneName}”，请检查 Build Settings。",
                this);
            return;
        }

        _isLoadingMainMenu = true;
        SetPauseControlsInteractable(false);
        Time.timeScale = 1f;

        AsyncOperation loadOperation = SceneManager.LoadSceneAsync(
            mainMenuSceneName,
            LoadSceneMode.Single);
        if (loadOperation != null)
        {
            return;
        }

        // Unity 正常情况下会返回 AsyncOperation；保底恢复暂停状态并允许再次尝试。
        _isLoadingMainMenu = false;
        SetPauseControlsInteractable(true);
        ApplyPauseState();
        Debug.LogError($"主菜单场景“{mainMenuSceneName}”未能创建加载任务。", this);
    }

    /// <summary>解析玩家生命、控制器与刚体引用，允许场景未手动绑定时安全回退。</summary>
    private void ResolvePlayerReferences()
    {
        if (playerHealth != null && playerController != null && playerRigidbody != null)
        {
            return;
        }

        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player == null)
        {
            Debug.LogError("GameFlowManager 找不到 Player，无法建立死亡流程。", this);
            return;
        }

        if (playerHealth == null)
        {
            playerHealth = player.GetComponent<PlayerHealth>();
        }

        if (playerController == null)
        {
            playerController = player.GetComponent<PlayerController>();
        }

        if (playerRigidbody == null)
        {
            playerRigidbody = player.GetComponent<Rigidbody2D>();
        }
    }

    /// <summary>绑定暂停和重新开始按钮的运行时点击事件。</summary>
    private void BindButtons()
    {
        if (resumeButton != null)
        {
            resumeButton.onClick.AddListener(ResumeGame);
        }

        if (pauseRestartButton != null)
        {
            pauseRestartButton.onClick.AddListener(RestartGame);
        }

        if (pauseMainMenuButton != null)
        {
            pauseMainMenuButton.onClick.AddListener(ReturnToMainMenu);
        }

        if (gameOverRestartButton != null)
        {
            gameOverRestartButton.onClick.AddListener(RestartGame);
        }
    }

    /// <summary>解除本组件添加的按钮点击事件。</summary>
    private void UnbindButtons()
    {
        if (resumeButton != null)
        {
            resumeButton.onClick.RemoveListener(ResumeGame);
        }

        if (pauseRestartButton != null)
        {
            pauseRestartButton.onClick.RemoveListener(RestartGame);
        }

        if (pauseMainMenuButton != null)
        {
            pauseMainMenuButton.onClick.RemoveListener(ReturnToMainMenu);
        }

        if (gameOverRestartButton != null)
        {
            gameOverRestartButton.onClick.RemoveListener(RestartGame);
        }
    }

    /// <summary>统一控制暂停菜单内按钮的可交互状态。</summary>
    private void SetPauseControlsInteractable(bool interactable)
    {
        if (resumeButton != null)
        {
            resumeButton.interactable = interactable;
        }

        if (pauseRestartButton != null)
        {
            pauseRestartButton.interactable = interactable;
        }

        if (pauseMainMenuButton != null)
        {
            pauseMainMenuButton.interactable = interactable;
        }
    }

    /// <summary>订阅一次玩家死亡事件，防止组件重复启用造成重复回调。</summary>
    private void SubscribePlayerDeath()
    {
        ResolvePlayerReferences();
        if (_playerDeathSubscribed || playerHealth == null)
        {
            return;
        }

        playerHealth.Died += HandlePlayerDied;
        _playerDeathSubscribed = true;
    }

    /// <summary>取消玩家死亡事件订阅。</summary>
    private void UnsubscribePlayerDeath()
    {
        if (!_playerDeathSubscribed || playerHealth == null)
        {
            return;
        }

        playerHealth.Died -= HandlePlayerDied;
        _playerDeathSubscribed = false;
    }

    /// <summary>玩家生命归零时停止控制、冻结本局并显示游戏结束面板。</summary>
    private void HandlePlayerDied()
    {
        if (IsGameOver)
        {
            return;
        }

        bool wasManuallyPaused = IsManuallyPaused;
        _pauseReasons = PauseReason.GameOver;

        if (playerRigidbody != null)
        {
            playerRigidbody.velocity = Vector2.zero;
        }

        if (playerController != null)
        {
            playerController.enabled = false;
        }

        SetPanelActive(levelUpPanel, false);
        SetPanelActive(pausePanel, false);
        SetPanelActive(gameOverPanel, true);
        ApplyPauseState();

        if (wasManuallyPaused)
        {
            NotifyManualPauseChanged(false);
        }
    }

    /// <summary>添加暂停原因并立即同步全局时间状态。</summary>
    private void AddPauseReason(PauseReason reason)
    {
        _pauseReasons |= reason;
        ApplyPauseState();
    }

    /// <summary>移除指定暂停原因并立即同步全局时间状态。</summary>
    private void RemovePauseReason(PauseReason reason)
    {
        _pauseReasons &= ~reason;
        ApplyPauseState();
    }

    /// <summary>查询指定暂停原因是否存在。</summary>
    private bool HasPauseReason(PauseReason reason)
    {
        return (_pauseReasons & reason) != 0;
    }

    /// <summary>根据当前暂停原因统一设置游戏时间流速。</summary>
    private void ApplyPauseState()
    {
        Time.timeScale = IsPaused ? 0f : 1f;
    }

    /// <summary>集中发布手动暂停状态，供 HUD 等表现层按需展开附加信息。</summary>
    private void NotifyManualPauseChanged(bool isManuallyPaused)
    {
        ManualPauseChanged?.Invoke(isManuallyPaused);
    }

    /// <summary>安全设置可选界面的激活状态。</summary>
    private static void SetPanelActive(GameObject panel, bool active)
    {
        if (panel != null)
        {
            panel.SetActive(active);
        }
    }
}
