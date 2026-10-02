using UnityEngine;

/// <summary>持武展示与主近战实体交接同一素材、尺寸和姿势；回收后平滑回到当前瞄准方向。</summary>
public sealed class WeaponHeldView : MonoBehaviour
{
    private WeaponBase _weapon;
    private SpriteRenderer _renderer;
    private SpriteRenderer _attackVisual;
    private bool _initialized;
    public SpriteRenderer Renderer => _renderer;

    /// <summary>装备时创建无碰撞的表现节点；持续光环与环绕不会生成持武图像。</summary>
    public void Configure(WeaponBase weapon)
    {
        _weapon = weapon;
        weapon.HeldView = this;
        if (!weapon.UsesHeldMount)
        {
            if (_renderer != null) _renderer.enabled = false;
            enabled = false;
            return;
        }
        if (_renderer == null)
        {
            var visual = new GameObject("HeldVisual"); visual.transform.SetParent(transform, false);
            _renderer = visual.AddComponent<SpriteRenderer>();
            SpriteRenderer owner = weapon.OwnerTransform.GetComponentInChildren<SpriteRenderer>();
            if (owner != null) { _renderer.sortingLayerID = owner.sortingLayerID; _renderer.sortingOrder = owner.sortingOrder + 2; }
        }
        _renderer.sprite = weapon.weaponData.icon;
        if (!_initialized) { UpdateRestPose(1f); _initialized = true; }
    }

    /// <summary>记录本次主动作的表现引用；多发额外攻击由各自池化实体显示。</summary>
    public void FollowAttack(SpriteRenderer visual) { _attackVisual = visual; }

    /// <summary>池化动作释放前交还最终姿势，清除引用防止跟随被另一武器复用的对象。</summary>
    public void CompleteAttack(SpriteRenderer visual)
    {
        if (_attackVisual != visual || _renderer == null) return;
        CopyAttackPose();
        _attackVisual = null;
    }

    /// <summary>复制实际攻击世界姿态到持武子节点，父级缩放从世界尺寸中消除。</summary>
    private void CopyAttackPose()
    {
        _renderer.transform.position = _attackVisual.transform.position;
        _renderer.transform.rotation = _attackVisual.transform.rotation;
        Vector3 scale = _attackVisual.transform.lossyScale;
        Vector3 parent = transform.lossyScale;
        _renderer.transform.localScale = new Vector3(scale.x / parent.x, scale.y / parent.y, 1f);
    }

    /// <summary>新回合立即清除旧实体引用并恢复当前属性决定的休息姿势。</summary>
    public void ResetView() { _attackVisual = null; if (_renderer != null) UpdateRestPose(1f); }

    /// <summary>攻击期间缓存实际姿势；动作结束或范围改变时指数插值，避免缩放和方向瞬跳。</summary>
    private void LateUpdate()
    {
        if (_weapon == null || _weapon.weaponData == null || _renderer == null) return;
        bool attacking = _attackVisual != null && _attackVisual.gameObject.activeInHierarchy;
        _renderer.enabled = _weapon.enabled && RoundController.AllowsCombat && !attacking;
        if (attacking)
        {
            CopyAttackPose();
            return;
        }
        _attackVisual = null;
        UpdateRestPose(1f - Mathf.Exp(-25f * Time.deltaTime));
    }

    /// <summary>沿瞄准方向摆放素材，使用与实际攻击相同的投影长度，属性在暂停中不推进动画。</summary>
    private void UpdateRestPose(float blend)
    {
        Vector2 direction = _weapon.CurrentAimDirection;
        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        float size = _weapon.CurrentVisualLength / WeaponVisualGeometry.ProjectedLength(_renderer.sprite, _weapon.weaponData.visualAngleOffset);
        float offset = _weapon.weaponData.runtimeType == WeaponRuntimeType.Melee ? WeaponVisualGeometry.MeleeCenter(_weapon.CurrentVisualRange) : 0f;
        _renderer.transform.localPosition = Vector3.Lerp(_renderer.transform.localPosition, (Vector3)direction * offset, blend);
        _renderer.transform.rotation = Quaternion.Slerp(_renderer.transform.rotation,
            Quaternion.Euler(0f, 0f, angle + _weapon.weaponData.visualAngleOffset), blend);
        _renderer.transform.localScale = Vector3.Lerp(_renderer.transform.localScale, new Vector3(size, size, 1f), blend);
        _renderer.flipY = false;
    }
}
