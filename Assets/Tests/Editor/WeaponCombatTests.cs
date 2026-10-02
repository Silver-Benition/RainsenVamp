using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>覆盖挂点、目标有效性、目录退出与真实正式资产配置。</summary>
public sealed class WeaponCombatTests
{
    private readonly List<Object> _objects = new List<Object>();
    /// <summary>销毁测试独占对象，不读写真实账号。</summary>
    [TearDown] public void Cleanup()
    { for (int i = _objects.Count - 1; i >= 0; i--) if (_objects[i] != null) Object.DestroyImmediate(_objects[i]); _objects.Clear(); }
    /// <summary>创建可追踪的测试对象。</summary>
    private GameObject ObjectAt(string name, Vector3 position)
    { var go = new GameObject(name); _objects.Add(go); go.transform.position = position; return go; }
    /// <summary>显式驱动 EditMode 生命周期。</summary>
    private static void Invoke(object instance, string name)
    { instance.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(instance, null); }

    /// <summary>实际持有对象在一至六槽及缩槽后保持等半径与等夹角，不合并重复内容。</summary>
    [Test] public void Mounts_OneThroughSix_AndRemoval_ReflowDistinctInstances()
    {
        var player = ObjectAt("Player", Vector3.zero);
        var layout = player.AddComponent<WeaponMountLayout>();
        var data = ScriptableObject.CreateInstance<WeaponDataSO>(); _objects.Add(data);
        var weapons = new List<WeaponBase>();
        for (int count = 1; count <= 6; count++)
        {
            var go = ObjectAt("DuplicateWeapon", Vector3.zero); go.transform.SetParent(player.transform);
            var weapon = go.AddComponent<WeaponBase>(); weapon.weaponData = data; weapons.Add(weapon); layout.Refresh(weapons);
            float radius = weapons[0].transform.localPosition.magnitude;
            for (int i = 0; i < count; i++)
            {
                Vector3 point = weapons[i].transform.localPosition;
                Assert.That(point.magnitude, Is.EqualTo(radius).Within(.001));
                float angle = Mathf.Repeat(Mathf.Atan2(point.y,point.x)*Mathf.Rad2Deg,360);
                Assert.That(angle,Is.EqualTo(360f*i/count).Within(.001));
                Assert.NotNull(weapons[i].GetComponentInChildren<SpriteRenderer>());
            }
        }
        weapons.RemoveAt(2); layout.Refresh(weapons);
        Assert.That(Vector3.Angle(weapons[0].transform.localPosition,weapons[1].transform.localPosition),Is.EqualTo(72).Within(.01));
    }

    /// <summary>最近敌人必须排除死亡对象，手动模式可明确覆盖自动方向。</summary>
    [Test] public void Aim_NearestLivingEnemy_ManualOverride_AndPoolGeneration()
    {
        var player = ObjectAt("AimOwner",Vector3.zero); var aim = player.AddComponent<AimController>(); Invoke(aim,"Awake");
        var near = EnemyAt(new Vector3(0,2)); var far = EnemyAt(new Vector3(-4,0));
        Physics2D.SyncTransforms();
        Assert.That(Vector2.Dot(aim.DirectionFrom(Vector2.zero),Vector2.up),Is.GreaterThan(.99));
        uint oldGeneration=near.LifeGeneration;
        Invoke(near,"OnDisable"); Invoke(near,"OnEnable");
        Assert.IsFalse(WeaponTargeting.IsValid(near.GetComponent<Collider2D>(),oldGeneration));
        near.gameObject.SetActive(false); aim.SetMode(AimController.AimMode.NearestEnemy);
        Assert.That(Vector2.Dot(aim.DirectionFrom(Vector2.zero),Vector2.left),Is.GreaterThan(.99));
        aim.SetManualDirection(Vector2.down); aim.SetMode(AimController.AimMode.Manual);
        Assert.AreEqual(Vector2.down,aim.DirectionFrom(Vector2.one));
        aim.SetManualDirection(Vector2.zero); Assert.AreEqual(Vector2.down,aim.DirectionFrom(Vector2.zero));
    }

    /// <summary>创建静止、存活的实际敌人组件作为物理查询目标。</summary>
    private EnemyBase EnemyAt(Vector3 position)
    {
        var go=ObjectAt("Target",position); go.layer=LayerMask.NameToLayer("Enemy");
        go.AddComponent<Rigidbody2D>().bodyType=RigidbodyType2D.Kinematic; go.AddComponent<CircleCollider2D>();
        var enemy=go.AddComponent<EnemyBase>(); Invoke(enemy,"Awake"); Invoke(enemy,"OnEnable");
        typeof(EnemyBase).GetField("_currentHealth",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(enemy,100f);
        return enemy;
    }

    /// <summary>退出池的三件旧武器在正式目录、商店和初始装备中均不可达，历史资产仍保留。</summary>
    [Test] public void RetiredWeapons_AbsentFromAllFormalCatalogs_StartingHammerAndSpearPattern()
    {
        var catalog=AssetDatabase.LoadAssetAtPath<GameContentCatalogSO>("Assets/Data/GameContentCatalog.asset");
        var shop=AssetDatabase.LoadAssetAtPath<RunShopCatalogSO>("Assets/Data/Rounds/ShopCatalog.asset");
        foreach(string name in new[]{"Knife","Axe","Aura"})
        {
            var data=AssetDatabase.LoadAssetAtPath<WeaponDataSO>("Assets/Data/"+name+".asset"); Assert.IsTrue(data.retiredFromPool);
            CollectionAssert.DoesNotContain(catalog.Weapons,data);
            foreach(var upgrade in catalog.Upgrades) Assert.AreNotSame(data,upgrade.weaponToGrant);
            foreach(var product in shop.products) Assert.AreNotSame(data,product.content.weaponToGrant);
        }
        foreach(var character in catalog.Characters) Assert.IsFalse(character.startingWeapon.retiredFromPool);
        var blue=AssetDatabase.LoadAssetAtPath<CharacterDataSO>("Assets/Data/Characters/BlueWarrior.asset");
        Assert.AreEqual("10_meteor_hammer",blue.startingWeapon.weaponID);
        var spear=AssetDatabase.LoadAssetAtPath<WeaponDataSO>("Assets/Data/ContentExpansion/Weapons/03_tide_spear.asset");
        Assert.AreEqual(MeleeAttackPattern.Alternating,spear.meleePattern);
        foreach(string id in new[]{"06_prism_shard","07_medical_lancet"})
            Assert.AreEqual(-45,AssetDatabase.LoadAssetAtPath<WeaponDataSO>("Assets/Data/ContentExpansion/Weapons/"+id+".asset").visualAngleOffset);
    }
}
