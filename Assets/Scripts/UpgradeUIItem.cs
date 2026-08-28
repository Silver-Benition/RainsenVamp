using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class UpgradeUIItem : MonoBehaviour
{
    [Header("UI 组件绑定")]
    public TextMeshProUGUI nameText;
    public TextMeshProUGUI descText;
    public TextMeshProUGUI levelText;   // 显示当前等级（可选，Inspector 不绑定则跳过）
    public Image iconImage;
    public Button button;

    private UpgradeDataSO currentData;

    /// <summary>
    /// 接收升级数据并刷新 UI 显示。
    /// </summary>
    public void Setup(UpgradeDataSO data)
    {
        currentData = data;

        // 查询当前武器等级
        WeaponBase owned = null;
        int currentLv = 0;
        if (data.weaponToGrant != null)
        {
            owned = LevelUpManager.Instance.GetOwnedWeapon(data.weaponToGrant);
            currentLv = owned != null ? owned.CurrentLevel : 0;
        }
        bool isFirstGet = currentLv == 0;

        // --- 名称 ---
        if (nameText != null)
        {
            string baseName = data.upgradeName;
            if (string.IsNullOrEmpty(baseName) && data.weaponToGrant != null)
                baseName = data.weaponToGrant.weaponNameKey;
            if (string.IsNullOrEmpty(baseName))
                baseName = data.name;

            nameText.text = isFirstGet
                ? $"获得 {baseName}"
                : $"{baseName} 等级 {currentLv + 1}";
        }

        // --- 描述：首次获得用原描述；已拥有则生成升级数值变化文本 ---
        if (descText != null)
        {
            if (isFirstGet)
            {
                string displayDesc = data.description;
                if (string.IsNullOrEmpty(displayDesc) && data.weaponToGrant != null)
                    displayDesc = data.weaponToGrant.descriptionKey;
                descText.text = displayDesc ?? string.Empty;
            }
            else
            {
                descText.text = BuildLevelUpDesc(data.weaponToGrant, currentLv);
            }
        }

        // --- 等级：不再显示 Lv.x/y，nameText 里已包含等级信息 ---
        if (levelText != null)
            levelText.text = string.Empty;

        // --- 图标：武器自身配置是统一来源，旧升级资产图标作为兼容回退 ---
        if (iconImage != null)
        {
            Sprite displayIcon = data.weaponToGrant != null && data.weaponToGrant.icon != null
                ? data.weaponToGrant.icon
                : data.icon;
            iconImage.sprite = displayIcon;
            iconImage.enabled = displayIcon != null;
        }

        // --- 按钮事件 ---
        if (button != null)
        {
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(OnButtonClicked);
        }
    }

    /// <summary>
    /// 把当前按钮绑定的升级数据交给全局升级管理器。
    /// </summary>
    private void OnButtonClicked()
    {
        if (LevelUpManager.Instance != null)
        {
            LevelUpManager.Instance.HandleCandidateSelected(currentData);
        }
    }

    /// <summary>
    /// 对比当前等级和下一等级的数值差异，生成多行升级描述文本。
    /// 优先使用 UpgradeDataSO 上配置的自定义描述，没有则自动生成。
    /// </summary>
    private string BuildLevelUpDesc(WeaponDataSO weaponData, int currentLv)
    {
        if (weaponData == null) return string.Empty;

        // 优先读自定义描述
        if (currentData != null && currentData.customLevelDescs != null)
        {
            int targetLv = currentLv + 1;
            var custom = currentData.customLevelDescs.Find(x => x.level == targetLv);
            if (custom != null && !string.IsNullOrEmpty(custom.customDesc))
                return custom.customDesc;
        }

        WeaponLevelData current = weaponData.GetLevelConfig(currentLv);
        WeaponLevelData next = weaponData.GetLevelConfig(currentLv + 1);
        var parts = new System.Collections.Generic.List<string>();

        AppendFloatDifference(parts, "伤害", current.damage, next.damage);
        AppendFloatDifference(parts, "冷却", current.cooldown, next.cooldown, "s");

        switch (weaponData.runtimeType)
        {
            case WeaponRuntimeType.Projectile:
                AppendIntDifference(parts, "发射数", current.projectileCount, next.projectileCount);
                AppendFloatDifference(parts, "速度", current.projectileSpeed, next.projectileSpeed);
                AppendIntDifference(parts, "穿透", current.pierceCount, next.pierceCount);
                AppendIntDifference(parts, "弹射", current.bounceCount, next.bounceCount);
                break;
            case WeaponRuntimeType.Aura:
                AppendFloatDifference(parts, "光环范围", current.auraRadius, next.auraRadius);
                AppendFloatDifference(parts, "伤害间隔", current.tickInterval, next.tickInterval, "s");
                break;
            case WeaponRuntimeType.Orbiting:
                AppendIntDifference(parts, "旋刃数量", current.projectileCount, next.projectileCount);
                AppendFloatDifference(parts, "环绕半径", current.orbitRadius, next.orbitRadius);
                AppendFloatDifference(parts, "环绕速度", current.orbitAngularSpeed, next.orbitAngularSpeed, "°/s");
                break;
            case WeaponRuntimeType.Lobbed:
                AppendIntDifference(parts, "飞斧数量", current.projectileCount, next.projectileCount);
                AppendFloatDifference(parts, "投掷力度", current.projectileSpeed, next.projectileSpeed);
                AppendIntDifference(parts, "穿透", current.pierceCount, next.pierceCount);
                AppendFloatDifference(parts, "下坠重力", current.lobGravity, next.lobGravity);
                break;
            case WeaponRuntimeType.Melee:
                AppendFloatDifference(parts, "攻击范围", current.meleeRange, next.meleeRange);
                AppendFloatDifference(parts, "挥击角度", current.meleeArc, next.meleeArc, "°");
                AppendFloatDifference(parts, "判定时间", current.activeDuration, next.activeDuration, "s");
                break;
        }

        return parts.Count > 0
            ? string.Join("\n", parts)
            : "属性强化";
    }

    /// <summary>
    /// 当浮点属性发生可见变化时追加一行差异描述。
    /// </summary>
    private void AppendFloatDifference(
        System.Collections.Generic.List<string> parts,
        string label,
        float currentValue,
        float nextValue,
        string suffix = "")
    {
        float difference = nextValue - currentValue;
        if (Mathf.Abs(difference) > 0.001f)
        {
            parts.Add($"{label}  {FormatDiff(difference)}{suffix}");
        }
    }

    /// <summary>
    /// 当整数属性变化时追加一行差异描述。
    /// </summary>
    private void AppendIntDifference(
        System.Collections.Generic.List<string> parts,
        string label,
        int currentValue,
        int nextValue)
    {
        int difference = nextValue - currentValue;
        if (difference != 0)
        {
            parts.Add($"{label}  {FormatDiff(difference)}");
        }
    }

    /// <summary>
    /// 把浮点差值格式化为带正负号的紧凑文本。
    /// </summary>
    private string FormatDiff(float diff)
        => diff > 0 ? $"+{diff:0.##}" : $"{diff:0.##}";

    /// <summary>
    /// 把整数差值格式化为带正负号的紧凑文本。
    /// </summary>
    private string FormatDiff(int diff)
        => diff > 0 ? $"+{diff}" : $"{diff}";
}
