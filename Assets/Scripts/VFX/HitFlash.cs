using System.Collections;
using UnityEngine;

/// <summary>
/// 受击闪白组件。
/// 挂在需要受击反馈的实体（怪物/Boss）根节点或含 SpriteRenderer 的子节点上。
/// 通过 MaterialPropertyBlock 驱动 Shader 的 _FlashAmount 属性，
/// 零材质实例分配、零 GC，适合海量同屏实体。
///
/// 使用前提：
/// - SpriteRenderer 的 Material 必须使用 "Custom/Sprites-FlashWhite" Shader
/// - 或任何包含 _FlashAmount 属性的兼容 Shader
/// </summary>
public class HitFlash : MonoBehaviour
{
    [Header("闪白配置")]
    [Tooltip("闪白持续时间（秒）。像素游戏建议极短，1~3 帧的视觉冲击最佳。")]
    [SerializeField] private float flashDuration = 0.1f;

    [Tooltip("闪白颜色。默认纯白，可改为其他色调（如红色表示高伤）。")]
    [SerializeField] private Color flashColor = Color.white;

    // 组件缓存
    private SpriteRenderer spriteRenderer;
    private MaterialPropertyBlock mpb;

    // Shader 属性 ID 缓存（避免每帧字符串查找）
    private static readonly int FlashAmountID = Shader.PropertyToID("_FlashAmount");
    private static readonly int FlashColorID = Shader.PropertyToID("_FlashColor");

    // 协程句柄：防止多次闪白叠加导致提前恢复
    private Coroutine flashCoroutine;

    private void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer == null)
        {
            spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        }

        mpb = new MaterialPropertyBlock();
    }

    /// <summary>
    /// 触发一次闪白。可在 TakeDamage() 中调用。
    /// 若前一次闪白尚未结束，会打断并重新开始（保证每次受击都有反馈）。
    /// </summary>
    public void TriggerFlash()
    {
        if (spriteRenderer == null) return;

        // 打断上一次未完成的闪白协程
        if (flashCoroutine != null)
        {
            StopCoroutine(flashCoroutine);
        }
        flashCoroutine = StartCoroutine(FlashRoutine());
    }

    /// <summary>
    /// 允许外部动态修改闪白颜色（如不同伤害类型用不同色调）。
    /// </summary>
    public void TriggerFlash(Color overrideColor)
    {
        flashColor = overrideColor;
        TriggerFlash();
    }

    private IEnumerator FlashRoutine()
    {
        // 设置闪白参数
        spriteRenderer.GetPropertyBlock(mpb);
        mpb.SetColor(FlashColorID, flashColor);
        mpb.SetFloat(FlashAmountID, 1f);
        spriteRenderer.SetPropertyBlock(mpb);

        // 等待闪白持续时间（使用 unscaledDeltaTime 确保暂停时不受影响）
        // 但受击闪白发生在游戏进行中，用 WaitForSeconds 即可（受 timeScale 影响是正确的）
        yield return new WaitForSeconds(flashDuration);

        // 恢复正常
        spriteRenderer.GetPropertyBlock(mpb);
        mpb.SetFloat(FlashAmountID, 0f);
        spriteRenderer.SetPropertyBlock(mpb);

        flashCoroutine = null;
    }

    /// <summary>
    /// 对象被回收（OnDisable）时立即重置闪白状态，
    /// 防止下次从对象池取出时还残留白色。
    /// </summary>
    private void OnDisable()
    {
        if (flashCoroutine != null)
        {
            StopCoroutine(flashCoroutine);
            flashCoroutine = null;
        }

        // 重置 MaterialPropertyBlock
        if (spriteRenderer != null && mpb != null)
        {
            spriteRenderer.GetPropertyBlock(mpb);
            mpb.SetFloat(FlashAmountID, 0f);
            spriteRenderer.SetPropertyBlock(mpb);
        }
    }
}
