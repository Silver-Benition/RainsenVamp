using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 波次刷怪配置（数据驱动）。
/// 设计目标：
/// 1) 运行时只做轻量计时与采样，生成统一走 PoolManager，避免 Instantiate/Destroy。
/// 2) 通过“时间窗 + 速率 + 并发上限”即可覆盖 Survivor-like 的心流节奏控制。
/// 3) 为后续 X 变量（无缝切换地图）预留：只需替换当前 WaveConfigSO 即可切换敌人池与规则。
/// </summary>
[CreateAssetMenu(fileName = "WaveConfig", menuName = "GameData/Wave/Wave Config")]
public class WaveConfigSO : ScriptableObject
{
    [Header("波次基础设置")]
    [Tooltip("这一套配置的总时长（秒）。到点后 WaveManager 会停止生成。0 或负数代表不限制。")]
    public float duration = 0f;

    [Header("刷怪规则（时间轴）")]
    public List<SpawnRule> rules = new List<SpawnRule>();

    [Serializable]
    public class SpawnRule
    {
        [Header("目标敌人")]
        public GameObject enemyPrefab;

        [Header("时间窗（秒）")]
        [Tooltip("从 0 秒开始计时。进入该时间点后开始生效。")]
        public float startTime = 0f;
        [Tooltip("超过该时间点后失效。<=0 代表永不结束。")]
        public float endTime = 0f;

        [Header("生成速率")]
        [Tooltip("每秒生成数量（支持小数）。例如 2.5 代表平均每秒 2~3 只。")]
        public float spawnsPerSecond = 1f;

        [Header("并发上限（性能闸门）")]
        [Tooltip("该规则同时在场的最大怪物数。<=0 代表不限制。")]
        public int maxAlive = 0;

        [Header("生成位置（以玩家为圆环）")]
        [Tooltip("最小生成半径。建议 >= 摄像机半对角线，避免怪物贴脸刷出。")]
        public float spawnRadiusMin = 6f;
        [Tooltip("最大生成半径。")]
        public float spawnRadiusMax = 10f;
    }
}

