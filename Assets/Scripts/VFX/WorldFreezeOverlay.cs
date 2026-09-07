using UnityEngine;

/// <summary>
/// 水晶球冻结期间的世界蓝色蒙版表现。
/// 组件仅监听冻结权威状态并控制 SpriteRenderer 显隐，不参与冻结逻辑与计时。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(SpriteRenderer))]
public sealed class WorldFreezeOverlay : MonoBehaviour
{
    [Header("依赖")]
    [SerializeField] private WorldFreezeController freezeController;

    private SpriteRenderer _spriteRenderer;
    private WorldFreezeController _subscribedController;

    /// <summary>蒙版当前是否可见，供运行时验收与自动化测试读取。</summary>
    public bool IsVisible => _spriteRenderer != null && _spriteRenderer.enabled;

    /// <summary>缓存同对象渲染器，避免状态切换时重复查询组件。</summary>
    private void Awake()
    {
        _spriteRenderer = GetComponent<SpriteRenderer>();
    }

    /// <summary>订阅冻结事件，并立即同步当前冻结状态。</summary>
    private void OnEnable()
    {
        if (_spriteRenderer == null)
        {
            _spriteRenderer = GetComponent<SpriteRenderer>();
        }

        Subscribe();
        SetVisible(IsControllerFrozen());
    }

    /// <summary>解除事件订阅，确保场景退出和对象复用时不会残留回调。</summary>
    private void OnDisable()
    {
        Unsubscribe();
        SetVisible(false);
    }

    /// <summary>绑定场景显式引用；缺失时仅使用权威实例作为安全降级。</summary>
    private void Subscribe()
    {
        WorldFreezeController target = freezeController != null
            ? freezeController
            : WorldFreezeController.Instance;
        if (target == null || target == _subscribedController)
        {
            return;
        }

        Unsubscribe();
        _subscribedController = target;
        _subscribedController.FreezeStateChanged += HandleFreezeStateChanged;
    }

    /// <summary>解除当前冻结控制器的事件绑定。</summary>
    private void Unsubscribe()
    {
        if (_subscribedController == null)
        {
            return;
        }

        _subscribedController.FreezeStateChanged -= HandleFreezeStateChanged;
        _subscribedController = null;
    }

    /// <summary>响应冻结状态边沿，仅切换蒙版显隐。</summary>
    private void HandleFreezeStateChanged(bool isFrozen)
    {
        SetVisible(isFrozen);
    }

    /// <summary>读取绑定控制器的实例状态，避免依赖其他场景中的静态实例。</summary>
    private bool IsControllerFrozen()
    {
        WorldFreezeController target = freezeController != null
            ? freezeController
            : _subscribedController;
        return target != null && target.isActiveAndEnabled && target.RemainingDuration > 0f;
    }

    /// <summary>安全更新蒙版渲染器显隐。</summary>
    private void SetVisible(bool visible)
    {
        if (_spriteRenderer != null)
        {
            _spriteRenderer.enabled = visible;
        }
    }
}
