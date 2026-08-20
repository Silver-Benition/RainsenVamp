using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 管理主菜单的输入焦点、游戏场景加载、版本展示与应用退出。
/// UI 结构由场景持有，本组件只负责行为绑定，避免运行时重复创建静态界面对象。
/// </summary>
public sealed class MainMenuController : MonoBehaviour
{
    [Header("Scene")]
    [SerializeField] private string gameplaySceneName = "MainLevel";

    [Header("Controls")]
    [SerializeField] private Button startButton;
    [SerializeField] private Button quitButton;

    [Header("Version")]
    [SerializeField] private TMP_Text versionText;
    [SerializeField] private string versionFormat = "v{0}";

    private bool _isLoading;
    private Coroutine _selectionCoroutine;

    /// <summary>指示主菜单是否已经开始加载游戏场景，用于阻止重复提交。</summary>
    public bool IsLoading => _isLoading;

    /// <summary>返回当前配置的游戏场景名，供诊断和自动化测试读取。</summary>
    public string GameplaySceneName => gameplaySceneName;

    /// <summary>校验必要引用，并确保从暂停状态返回主菜单时恢复正常时间流速。</summary>
    private void Awake()
    {
        Time.timeScale = 1f;

        if (startButton != null && quitButton != null && versionText != null)
        {
            return;
        }

        Debug.LogError(
            "MainMenuController 缺少必要的按钮或版本文本引用，主菜单交互已停用。",
            this);
        enabled = false;
    }

    /// <summary>绑定按钮事件、刷新版本文本，并在下一帧建立键盘与手柄焦点。</summary>
    private void OnEnable()
    {
        startButton.onClick.AddListener(StartGame);
        quitButton.onClick.AddListener(QuitGame);
        versionText.text = string.Format(versionFormat, Application.version);

        _selectionCoroutine = StartCoroutine(SelectDefaultButtonNextFrame());
    }

    /// <summary>解除运行时事件和待执行协程，防止组件反复启用后产生重复回调。</summary>
    private void OnDisable()
    {
        if (startButton != null)
        {
            startButton.onClick.RemoveListener(StartGame);
        }

        if (quitButton != null)
        {
            quitButton.onClick.RemoveListener(QuitGame);
        }

        if (_selectionCoroutine != null)
        {
            StopCoroutine(_selectionCoroutine);
            _selectionCoroutine = null;
        }
    }

    /// <summary>
    /// 开始异步加载游戏场景；一旦提交便立即锁定按钮，避免快速连点创建重复加载请求。
    /// 如果目标场景未加入 Build Settings，则保留当前页面并输出明确错误。
    /// </summary>
    private void StartGame()
    {
        if (_isLoading)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(gameplaySceneName) ||
            !Application.CanStreamedLevelBeLoaded(gameplaySceneName))
        {
            Debug.LogError(
                $"无法加载游戏场景“{gameplaySceneName}”，请检查 Build Settings。",
                this);
            return;
        }

        _isLoading = true;
        SetControlsInteractable(false);
        Time.timeScale = 1f;

        AsyncOperation loadOperation = SceneManager.LoadSceneAsync(
            gameplaySceneName,
            LoadSceneMode.Single);
        if (loadOperation != null)
        {
            return;
        }

        // Unity 正常情况下会返回 AsyncOperation；保底恢复能让异常平台仍可重新尝试。
        _isLoading = false;
        SetControlsInteractable(true);
        Debug.LogError($"游戏场景“{gameplaySceneName}”未能创建加载任务。", this);
    }

    /// <summary>在正式构建中退出应用；Editor 内保持运行，避免误关编辑器或测试进程。</summary>
    private void QuitGame()
    {
#if !UNITY_EDITOR
        Application.Quit();
#endif
    }

    /// <summary>统一控制两个按钮的可交互状态，确保场景加载期间不再接受输入。</summary>
    /// <param name="interactable">按钮是否允许交互。</param>
    private void SetControlsInteractable(bool interactable)
    {
        startButton.interactable = interactable;
        quitButton.interactable = interactable;
    }

    /// <summary>
    /// 等待 EventSystem 完成启用后默认选中开始按钮，使键盘和手柄进入页面即可导航。
    /// </summary>
    private IEnumerator SelectDefaultButtonNextFrame()
    {
        yield return null;

        _selectionCoroutine = null;
        EventSystem eventSystem = EventSystem.current;
        if (eventSystem == null || startButton == null || !startButton.interactable)
        {
            yield break;
        }

        // 先清空再重新设置，可处理 EventSystem 仍保留旧场景焦点的情况。
        eventSystem.SetSelectedGameObject(null);
        eventSystem.SetSelectedGameObject(startButton.gameObject);
    }
}
