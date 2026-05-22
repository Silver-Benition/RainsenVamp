using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 玩家核心控制类
/// 职责：处理玩家输入、物理移动、以及基础的视觉状态同步
/// 架构：将输入获取（Update）、物理移动（FixedUpdate）与表现更新（Update）分离
/// </summary>

[RequireComponent(typeof(Rigidbody2D))]
public class PlayerController : MonoBehaviour
{
    [Header("移动参数")]
    [Tooltip("后备移动速度（仅当未找到 PlayerStats 时使用）")]
    [SerializeField] private float fallbackMoveSpeed = 3.0f;

    [Header("组件引用")]
    [Tooltip("角色动画控制器")]
    [SerializeField] private Animator animator;
    [Tooltip("角色精灵渲染器，用于翻转朝向")]
    [SerializeField] private SpriteRenderer spriteRenderer;

    // 内部缓存
    private Rigidbody2D rb;
    private PlayerStats playerStats;
    private Vector2 movementInput;

    // 动画参数哈希值缓存（性能优化：比直接传字符串快得多）
    private readonly int isMovingHash = Animator.StringToHash("IsMoving");

    private void Awake()
    {
        // 缓存自身组件，避免运行时 GetComponent 产生开销
        rb = GetComponent<Rigidbody2D>();
        playerStats = GetComponent<PlayerStats>();

        // 安全校验：如果未在面板拖拽赋值，尝试自动获取子物体的表现组件
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (spriteRenderer == null) spriteRenderer = GetComponentInChildren<SpriteRenderer>();
    }

    private void Update()
    {
        // 1. 逻辑层：获取输入 (使用 GetAxisRaw 确保没有摇杆惯性，实现像素游戏的干脆手感)
        float moveX = Input.GetAxisRaw("Horizontal");
        float moveY = Input.GetAxisRaw("Vertical");

        // 归一化向量：防止斜向移动时速度变成 1.414 倍
        movementInput = new Vector2(moveX, moveY).normalized;

        // 2. 表现层：更新视觉状态
        UpdateVisuals();
    }

    private void FixedUpdate()
    {
        // 物理层：在 FixedUpdate 中处理刚体移动，确保帧率波动时移动平滑且碰撞稳定
        // 从 PlayerStats 读取最终速度（含升级加成），找不到则用后备值
        float speed = playerStats != null ? playerStats.FinalMoveSpeed : fallbackMoveSpeed;
        rb.velocity = movementInput * speed;
    }

    /// <summary>
    /// 更新角色的视觉表现（朝向与动画）
    /// </summary>
    private void UpdateVisuals()
    {
        if (animator == null || spriteRenderer == null) return;

        // 判断是否正在移动
        bool isMoving = movementInput.sqrMagnitude > 0.01f;
        animator.SetBool(isMovingHash, isMoving);

        // 处理角色朝向翻转 (仅当有水平输入时才更新，保持垂直移动时的朝向)
        if (movementInput.x != 0)
        {
            // 美术素材默认朝左，所以向右移动(x > 0)时才需要翻转
            spriteRenderer.flipX = movementInput.x > 0;
        }
    }
}