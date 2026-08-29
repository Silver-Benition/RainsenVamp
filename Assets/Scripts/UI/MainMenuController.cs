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
    [SerializeField] private Button collectionButton;
    [SerializeField] private Button quitButton;
    [SerializeField] private CharacterSelectionUI characterSelectionUI;
    [SerializeField] private CollectionUI collectionUI;

    [Header("Version")]
    [SerializeField] private TMP_Text versionText;
    [SerializeField] private string versionFormat = "v{0}";

    private bool _isLoading;
    private Coroutine _selectionCoroutine;

    /// <summary>指示主菜单是否已经开始加载游戏场景，用于阻止重复提交。</summary>
    public bool IsLoading => _isLoading;

    /// <summary>返回当前配置的游戏场景名，供诊断和自动化测试读取。</summary>
    public string GameplaySceneName => gameplaySceneName;

    /// <summary>角色选择页当前是否打开。</summary>
    public bool IsCharacterSelectionVisible =>
        characterSelectionUI != null && characterSelectionUI.IsVisible;

    /// <summary>收藏页面当前是否打开。</summary>
    public bool IsCollectionVisible => collectionUI != null && collectionUI.IsVisible;

    /// <summary>校验必要引用，并确保从暂停状态返回主菜单时恢复正常时间流速。</summary>
    private void Awake()
    {
        Time.timeScale = 1f;
        CharacterSelectionSession.Clear();

        if (characterSelectionUI == null)
        {
            characterSelectionUI = FindObjectOfType<CharacterSelectionUI>(true);
        }

        if (startButton != null && collectionButton != null && quitButton != null &&
            characterSelectionUI != null && collectionUI != null && versionText != null)
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
        collectionButton.onClick.AddListener(OpenCollection);
        quitButton.onClick.AddListener(QuitGame);
        if (characterSelectionUI != null)
        {
            characterSelectionUI.CharacterConfirmed += BeginGameLoad;
            characterSelectionUI.Closed += HandleCharacterSelectionClosed;
        }
        if (collectionUI != null)
        {
            collectionUI.Closed += HandleCollectionClosed;
        }
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

        if (collectionButton != null)
        {
            collectionButton.onClick.RemoveListener(OpenCollection);
        }

        if (characterSelectionUI != null)
        {
            characterSelectionUI.CharacterConfirmed -= BeginGameLoad;
            characterSelectionUI.Closed -= HandleCharacterSelectionClosed;
        }
        if (collectionUI != null)
        {
            collectionUI.Closed -= HandleCollectionClosed;
        }

        if (_selectionCoroutine != null)
        {
            StopCoroutine(_selectionCoroutine);
            _selectionCoroutine = null;
        }
    }

    /// <summary>
    /// 从主菜单进入角色选择页；只有角色确认后才开始加载游戏场景。
    /// </summary>
    private void StartGame()
    {
        if (_isLoading)
        {
            return;
        }

        Time.timeScale = 1f;
        if (characterSelectionUI != null)
        {
            SetControlsInteractable(false);
            characterSelectionUI.Show();
            return;
        }

        Debug.LogWarning("主菜单未配置角色选择页，将直接使用场景内默认角色。", this);
        BeginGameLoad(null);
    }

    /// <summary>
    /// 保存确认角色并异步加载游戏场景；提交后立即锁定所有选择控件，避免重复加载。
    /// </summary>
    private void BeginGameLoad(CharacterDataSO character)
    {
        if (_isLoading)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(gameplaySceneName) ||
            !Application.CanStreamedLevelBeLoaded(gameplaySceneName))
        {
            if (characterSelectionUI != null)
            {
                characterSelectionUI.SetInteractionEnabled(true);
            }

            Debug.LogError(
                $"无法加载游戏场景“{gameplaySceneName}”，请检查 Build Settings。",
                this);
            return;
        }

        if (character != null)
        {
            AccountProgressService.Current.SetLastSelectedCharacter(character.characterID);
            CharacterSelectionSession.Select(character);
        }

        _isLoading = true;
        SetControlsInteractable(false);
        if (characterSelectionUI != null)
        {
            characterSelectionUI.SetInteractionEnabled(false);
        }
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
        CharacterSelectionSession.Clear();
        if (characterSelectionUI != null)
        {
            characterSelectionUI.SetInteractionEnabled(true);
        }
        Debug.LogError($"游戏场景“{gameplaySceneName}”未能创建加载任务。", this);
    }

    /// <summary>从角色选择页返回后恢复主菜单按钮和默认焦点。</summary>
    private void HandleCharacterSelectionClosed()
    {
        if (_isLoading)
        {
            return;
        }

        SetControlsInteractable(true);
        if (_selectionCoroutine != null)
        {
            StopCoroutine(_selectionCoroutine);
        }

        _selectionCoroutine = StartCoroutine(SelectDefaultButtonNextFrame());
    }

    /// <summary>锁定主菜单按钮并打开收藏页面。</summary>
    private void OpenCollection()
    {
        if (_isLoading || collectionUI == null)
        {
            return;
        }

        SetControlsInteractable(false);
        collectionUI.Show();
    }

    /// <summary>收藏页返回后恢复主菜单按钮和默认焦点。</summary>
    private void HandleCollectionClosed()
    {
        if (_isLoading)
        {
            return;
        }

        SetControlsInteractable(true);
        if (_selectionCoroutine != null)
        {
            StopCoroutine(_selectionCoroutine);
        }

        _selectionCoroutine = StartCoroutine(SelectDefaultButtonNextFrame());
    }

    /// <summary>在正式构建中退出应用；Editor 内保持运行，避免误关编辑器或测试进程。</summary>
    private void QuitGame()
    {
#if !UNITY_EDITOR
        Application.Quit();
#endif
    }

    /// <summary>统一控制三个主菜单按钮的可交互状态，确保子页面或场景加载期间不再接受输入。</summary>
    /// <param name="interactable">按钮是否允许交互。</param>
    private void SetControlsInteractable(bool interactable)
    {
        startButton.interactable = interactable;
        if (collectionButton != null)
        {
            collectionButton.interactable = interactable;
        }
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
