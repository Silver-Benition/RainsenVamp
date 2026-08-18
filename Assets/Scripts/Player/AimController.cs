using UnityEngine;

/// <summary>
/// 瞄准方向控制器。挂在 Player 上，为所有武器提供统一的"发射方向"。
///
/// 当前模式：
/// - FollowMovement：跟随玩家移动方向，停止移动时保持最后朝向
///
/// 未来扩展：
/// - Manual：由鼠标/右摇杆控制瞄准方向（移动方向 ≠ 攻击方向）
/// - NearestEnemy：自动锁定最近敌人方向
/// </summary>
public class AimController : MonoBehaviour
{
    public enum AimMode
    {
        FollowMovement = 0,  // 跟随移动方向（Vampire Survivors 经典模式）
        // Manual = 1,       // 手动瞄准（预留：鼠标/右摇杆）
        // NearestEnemy = 2, // 自动锁定最近敌人（预留）
    }

    [Header("瞄准模式")]
    public AimMode aimMode = AimMode.FollowMovement;

    [Header("默认朝向")]
    [Tooltip("游戏开始时 / 未产生任何输入前的默认发射方向")]
    [SerializeField] private Vector2 defaultDirection = Vector2.right;

    /// <summary>
    /// 当前瞄准方向（归一化）。武器系统读取此属性决定发射朝向。
    /// </summary>
    public Vector2 AimDirection { get; private set; }

    /// <summary>
    /// 最后一次有效水平输入的朝向符号；向右为 1，向左为 -1。
    /// </summary>
    public float HorizontalFacingSign { get; private set; } = 1f;

    /// <summary>
    /// 使用默认瞄准方向初始化瞄准向量与稳定水平朝向。
    /// </summary>
    private void Awake()
    {
        AimDirection = defaultDirection.normalized;
        HorizontalFacingSign = AimDirection.x < -0.01f ? -1f : 1f;
    }

    private void Update()
    {
        switch (aimMode)
        {
            case AimMode.FollowMovement:
                UpdateFollowMovement();
                break;

            // 未来模式在这里扩展
            // case AimMode.Manual:
            //     UpdateManualAim();
            //     break;
        }
    }

    /// <summary>
    /// 跟随移动方向模式：读取输入轴，有输入时更新方向，无输入时保持最后朝向。
    /// </summary>
    private void UpdateFollowMovement()
    {
        float moveX = Input.GetAxisRaw("Horizontal");
        float moveY = Input.GetAxisRaw("Vertical");

        Vector2 input = new Vector2(moveX, moveY);

        // 只在有实际输入时更新方向（停下时保持最后方向，和 Vampire Survivors 一致）
        if (input.sqrMagnitude > 0.01f)
        {
            AimDirection = input.normalized;
            if (Mathf.Abs(input.x) > 0.01f)
            {
                HorizontalFacingSign = Mathf.Sign(input.x);
            }
        }
    }

    // =======================================================================
    // 预留：手动瞄准模式（后续实现时取消注释并扩展）
    // =======================================================================
    // private void UpdateManualAim()
    // {
    //     // 鼠标方案：
    //     // Vector3 mouseWorld = Camera.main.ScreenToWorldPoint(Input.mousePosition);
    //     // AimDirection = ((Vector2)(mouseWorld - transform.position)).normalized;
    //
    //     // 右摇杆方案：
    //     // float aimX = Input.GetAxisRaw("RightStickHorizontal");
    //     // float aimY = Input.GetAxisRaw("RightStickVertical");
    //     // Vector2 aimInput = new Vector2(aimX, aimY);
    //     // if (aimInput.sqrMagnitude > 0.1f)
    //     //     AimDirection = aimInput.normalized;
    // }
}
