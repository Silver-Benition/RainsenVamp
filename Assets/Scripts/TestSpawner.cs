using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TestSpawner : MonoBehaviour
{
    public GameObject enemyPrefab;
    public float spawnInterval = 1f;
    private float timer;

    void Update()
    {
        timer += Time.deltaTime;
        if (timer >= spawnInterval)
        {
            // 在刷怪器位置随机偏移一点生成怪物
            Vector3 spawnPos = transform.position + (Vector3)Random.insideUnitCircle * 2f;
            PoolManager.Instance.Spawn(enemyPrefab, spawnPos, Quaternion.identity);
            timer = 0f;
        }
    }
}
