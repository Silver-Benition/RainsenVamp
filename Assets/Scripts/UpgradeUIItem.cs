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

        // --- 图标 ---
        if (iconImage != null && data.icon != null)
            iconImage.sprite = data.icon;

        // --- 按钮事件 ---
        if (button != null)
        {
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(OnButtonClicked);
        }
    }

    private void OnButtonClicked()
    {
        LevelUpManager.Instance.ApplyUpgrade(currentData);
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

        WeaponLevelData cur  = weaponData.GetLevelConfig(currentLv);
        WeaponLevelData next = weaponData.GetLevelConfig(currentLv + 1);

        var parts = new System.Collections.Generic.List<string>();

        float dmgDiff = next.damage - cur.damage;
        if (Mathf.Abs(dmgDiff) > 0.01f)
            parts.Add($"伤害  {FormatDiff(dmgDiff)}");

        float cdDiff = next.cooldown - cur.cooldown;
        if (Mathf.Abs(cdDiff) > 0.001f)
            parts.Add($"冷却  {FormatDiff(cdDiff)}s");

        int pierceDiff = next.pierceCount - cur.pierceCount;
        if (pierceDiff != 0)
            parts.Add($"穿刺  {FormatDiff(pierceDiff)}");

        float speedDiff = next.projectileSpeed - cur.projectileSpeed;
        if (Mathf.Abs(speedDiff) > 0.01f)
            parts.Add($"速度  {FormatDiff(speedDiff)}");

        int countDiff = next.projectileCount - cur.projectileCount;
        if (countDiff != 0)
            parts.Add($"发射数  {FormatDiff(countDiff)}");

        int bounceDiff = next.bounceCount - cur.bounceCount;
        if (bounceDiff != 0)
            parts.Add($"弹射  {FormatDiff(bounceDiff)}");

        float radiusDiff = next.auraRadius - cur.auraRadius;
        if (Mathf.Abs(radiusDiff) > 0.01f)
            parts.Add($"光环范围  {FormatDiff(radiusDiff)}");

        return parts.Count > 0
            ? string.Join("\n", parts)
            : "属性强化";
    }

    private string FormatDiff(float diff)
        => diff > 0 ? $"+{diff:0.##}" : $"{diff:0.##}";

    private string FormatDiff(int diff)
        => diff > 0 ? $"+{diff}" : $"{diff}";
}
