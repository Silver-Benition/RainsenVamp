using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerStats : MonoBehaviour
{
    [Header("等级与经验")]
    public int currentLevel = 1;
    public float currentExp = 0;
    public float expToNextLevel = 10;

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

    public void AddExp(float amount)
    {
        currentExp += amount;

        // 计算升了几级，加入队列
        while (currentExp >= expToNextLevel)
        {
            currentExp -= expToNextLevel;
            currentLevel++;
            expToNextLevel *= 1.2f;
            levelUpQueue++;
        }

        // 如果有排队的升级，且当前没有显示面板，则触发升级
        CheckLevelUpQueue();
    }

    public void CheckLevelUpQueue()
    {
        // 如果队列中有升级，并且游戏没有处于暂停状态（面板没开）
        if (levelUpQueue > 0 && Time.timeScale > 0f)
        {
            levelUpQueue--;
            LevelUpManager.Instance.ShowLevelUpUI();
        }
    }
}
