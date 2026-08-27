using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerStats : MonoBehaviour
{
    [Header("等级与经验")]
    public int currentLevel = 1;
    public float currentExp = 0;
    public float expToNextLevel = 10;

    [Tooltip("逐级经验需求：第 1 项表示 Lv.1 到 Lv.2，第 2 项表示 Lv.2 到 Lv.3，以此类推。")]
    [SerializeField] private List<float> experienceRequirements = new List<float>
    {
        10f,
        12f,
        15f,
        18f,
        21f,
        25f,
        30f,
        36f,
        43f,
        52f
    };

    [Tooltip("超过列表配置的等级后，以上一项经验需求乘此倍率继续增长。")]
    [Min(1f)]
    [SerializeField] private float experienceFallbackGrowth = 1.2f;

    [Header("移动属性")]
    [Tooltip("基础移动速度")]
    public float baseMoveSpeed = 3.0f;
    [Tooltip("速度加成（来自升级/道具，叠加计算）。0.2 = 加速 20%")]
    public float moveSpeedBonus = 0f;

    /// <summary>
    /// 最终移动速度 = 基础 × (1 + 加成百分比)。
    /// PlayerController 每帧读取此值。
    /// </summary>
    public float FinalMoveSpeed => baseMoveSpeed * (1f + moveSpeedBonus);

    private int levelUpQueue = 0; // 升级队列

    /// <summary>初始化当前等级对应的经验需求，确保场景序列化值与逐级配置同步。</summary>
    private void Awake()
    {
        currentLevel = Mathf.Max(1, currentLevel);
        expToNextLevel = GetExperienceRequiredForLevel(currentLevel);
    }

    /// <summary>
    /// 读取指定等级到下一级所需的经验。
    /// 等级从 1 开始；超过列表范围时按最后一项和备用倍率继续增长。
    /// </summary>
    /// <param name="level">当前等级。</param>
    /// <returns>从当前等级升到下一级所需的正数经验。</returns>
    public float GetExperienceRequiredForLevel(int level)
    {
        int normalizedLevel = Mathf.Max(1, level);
        int listIndex = normalizedLevel - 1;

        if (experienceRequirements != null &&
            listIndex < experienceRequirements.Count &&
            experienceRequirements[listIndex] > 0f)
        {
            return experienceRequirements[listIndex];
        }

        float fallbackRequirement = Mathf.Max(expToNextLevel, 1f);
        int fallbackStartIndex = 0;

        if (experienceRequirements != null)
        {
            for (int index = experienceRequirements.Count - 1; index >= 0; index--)
            {
                if (experienceRequirements[index] > 0f)
                {
                    fallbackRequirement = experienceRequirements[index];
                    fallbackStartIndex = index;
                    break;
                }
            }
        }

        int missingLevels = Mathf.Max(0, listIndex - fallbackStartIndex);
        float growth = Mathf.Max(experienceFallbackGrowth, 1f);
        return Mathf.Max(1f, fallbackRequirement * Mathf.Pow(growth, missingLevels));
    }

    public void AddExp(float amount)
    {
        if (amount <= 0f)
        {
            return;
        }

        currentExp += amount;

        if (expToNextLevel <= 0f)
        {
            expToNextLevel = GetExperienceRequiredForLevel(currentLevel);
        }

        // 计算升了几级，加入队列
        while (currentExp >= expToNextLevel)
        {
            currentExp -= expToNextLevel;
            currentLevel++;
            expToNextLevel = GetExperienceRequiredForLevel(currentLevel);
            levelUpQueue++;
        }

        // 如果有排队的升级，且当前没有显示面板，则触发升级
        CheckLevelUpQueue();
    }

    public void CheckLevelUpQueue()
    {
        // 如果队列中有升级，并且游戏没有处于暂停状态（面板没开）
        if (levelUpQueue > 0 && Time.timeScale > 0f && LevelUpManager.Instance != null)
        {
            levelUpQueue--;
            LevelUpManager.Instance.ShowLevelUpUI();
        }
    }
}
