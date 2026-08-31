using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace RainsenVampSur.Tests
{
    /// <summary>验证武器种类上限、候选过滤、有序登记和最终授予防线。</summary>
    public sealed class LevelUpManagerLoadoutTests : EditModeComponentTestBase
    {
        private readonly List<ScriptableObject> _createdData = new List<ScriptableObject>();

        /// <summary>每项装载测试使用独立账号，隔离起始武器收藏发现状态。</summary>
        [SetUp]
        public void ResetAccountProgress()
        {
            AccountProgressService.SetStorageForTests(new InMemoryAccountProgressStorage());
        }

        /// <summary>销毁测试创建的数据资产替身，避免内存对象残留到下一项测试。</summary>
        [TearDown]
        public void CleanUpLoadoutData()
        {
            for (int index = _createdData.Count - 1; index >= 0; index--)
            {
                if (_createdData[index] != null)
                {
                    Object.DestroyImmediate(_createdData[index]);
                }
            }

            _createdData.Clear();
        }

        /// <summary>六种场景预置武器应全部登记，并保持 Player 子物体顺序。</summary>
        [Test]
        public void RegisterDefaultWeapons_六种不同武器_按层级顺序登记()
        {
            LevelUpFixture fixture = CreateFixture(PlayerLoadoutRules.MaxWeaponCount);

            Assert.That(fixture.Manager.OwnedWeaponCount, Is.EqualTo(PlayerLoadoutRules.MaxWeaponCount));
            for (int index = 0; index < fixture.Weapons.Count; index++)
            {
                Assert.AreSame(fixture.Weapons[index], fixture.Manager.OwnedWeapons[index]);
            }

            Assert.IsTrue(fixture.Manager.CanAcquireWeapon(fixture.WeaponData[0]));
        }

        /// <summary>满六种后过滤全新武器，但已持有且未满级的武器仍保留升级候选。</summary>
        [Test]
        public void BuildSelectableUpgradePool_武器槽已满_只保留已有武器升级()
        {
            LevelUpFixture fixture = CreateFixture(PlayerLoadoutRules.MaxWeaponCount, true);
            UpgradeDataSO ownedUpgrade = CreateUpgradeData(fixture.WeaponData[0]);
            WeaponDataSO seventhWeapon = CreateWeaponData("weapon_7", false);
            UpgradeDataSO seventhUpgrade = CreateUpgradeData(seventhWeapon);
            fixture.Manager.allAvailableUpgrades = new List<UpgradeDataSO>
            {
                seventhUpgrade,
                ownedUpgrade
            };

            List<UpgradeDataSO> pool =
                TestObjectUtility.InvokeNonPublicMethod<List<UpgradeDataSO>>(
                    fixture.Manager,
                    "BuildSelectableUpgradePool");

            Assert.That(pool, Has.Count.EqualTo(1));
            Assert.Contains(ownedUpgrade, pool);
            Assert.IsFalse(pool.Contains(seventhUpgrade));
        }

        /// <summary>最终授予入口必须再次拒绝第七种武器，防止调试或旧按钮绕过候选过滤。</summary>
        [Test]
        public void DebugEnsureWeaponLevel_已有六种武器_拒绝第七种()
        {
            LevelUpFixture fixture = CreateFixture(PlayerLoadoutRules.MaxWeaponCount);
            WeaponDataSO seventhWeapon = CreateWeaponData("weapon_7", false);
            LogAssert.Expect(
                LogType.Warning,
                $"[LevelUpManager] 已达到 {PlayerLoadoutRules.MaxWeaponCount} 种武器上限，" +
                $"无法获得新武器：{seventhWeapon.weaponNameKey}");

            WeaponBase grantedWeapon = fixture.Manager.DebugEnsureWeaponLevel(seventhWeapon, 1);

            Assert.IsNull(grantedWeapon);
            Assert.That(fixture.Manager.OwnedWeaponCount, Is.EqualTo(PlayerLoadoutRules.MaxWeaponCount));
            Assert.IsFalse(fixture.Manager.CanAcquireWeapon(seventhWeapon));
        }

        /// <summary>存在空槽时获得新武器应追加到末尾，并只发布一次清单变化事件。</summary>
        [Test]
        public void DebugEnsureWeaponLevel_存在第六个空槽_追加武器并发布事件()
        {
            LevelUpFixture fixture = CreateFixture(PlayerLoadoutRules.MaxWeaponCount - 1);
            WeaponDataSO sixthWeapon = CreateWeaponData("weapon_6", false);
            int changedCount = 0;
            fixture.Manager.OwnedWeaponsChanged += () => changedCount++;

            WeaponBase grantedWeapon = fixture.Manager.DebugEnsureWeaponLevel(sixthWeapon, 1);

            Assert.IsNotNull(grantedWeapon);
            Assert.That(fixture.Manager.OwnedWeaponCount, Is.EqualTo(PlayerLoadoutRules.MaxWeaponCount));
            Assert.AreSame(grantedWeapon, fixture.Manager.OwnedWeapons[PlayerLoadoutRules.MaxWeaponCount - 1]);
            Assert.That(changedCount, Is.EqualTo(1));
        }

        /// <summary>相同稳定 ID 的重复组件只占一个种类槽位。</summary>
        [Test]
        public void RegisterDefaultWeapons_重复稳定ID_只登记一次()
        {
            GameObject player = CreateTrackedGameObject("AutomationTest_Player");
            WeaponDataSO duplicatedData = CreateWeaponData("duplicated_weapon", false);
            CreateWeaponComponent(player.transform, duplicatedData, "Weapon_A");
            CreateWeaponComponent(player.transform, duplicatedData, "Weapon_B");
            LevelUpManager manager = CreateManager(player.transform);

            Assert.That(manager.OwnedWeaponCount, Is.EqualTo(1));
            Assert.AreSame(duplicatedData, manager.OwnedWeapons[0].weaponData);
        }

        /// <summary>角色起始武器应只确保 Lv.1 存在，重复初始化不能把它当成普通升级。</summary>
        [Test]
        public void EnsureCharacterStartingWeapon_重复调用_只创建一把一级武器()
        {
            GameObject player = CreateTrackedGameObject("AutomationTest_StartingWeaponPlayer");
            CharacterDataSO character = ScriptableObject.CreateInstance<CharacterDataSO>();
            WeaponDataSO startingWeapon = CreateWeaponData("starter_weapon", true);
            character.characterID = "character_starter_test";
            character.startingWeapon = startingWeapon;
            _createdData.Add(character);

            PlayerStats stats = player.AddComponent<PlayerStats>();
            stats.SetCharacterData(character);
            LevelUpManager manager = CreateManager(player.transform);
            TestObjectUtility.SetPrivateField(manager, "_playerStats", stats);

            TestObjectUtility.InvokeNonPublicMethod(manager, "EnsureCharacterStartingWeapon");
            TestObjectUtility.InvokeNonPublicMethod(manager, "EnsureCharacterStartingWeapon");

            Assert.That(manager.OwnedWeaponCount, Is.EqualTo(1));
            Assert.That(manager.OwnedWeapons[0].weaponData, Is.SameAs(startingWeapon));
            Assert.That(manager.OwnedWeapons[0].CurrentLevel, Is.EqualTo(1));
        }

        /// <summary>候选必须恰好包含一种奖励；空奖励与同时配置武器能力的资产均被拒绝。</summary>
        [Test]
        public void BuildSelectableUpgradePool_奖励配置非法_不会进入候选()
        {
            GameObject player = CreateTrackedGameObject("AutomationTest_InvalidRewardPlayer");
            player.AddComponent<PlayerStats>();
            player.AddComponent<PlayerHealth>();
            AbilityManager abilityManager = player.AddComponent<AbilityManager>();
            LevelUpManager manager = CreateManager(player.transform);
            TestObjectUtility.SetPrivateField(manager, "_abilityManager", abilityManager);

            WeaponDataSO weapon = CreateWeaponData("invalid_reward_weapon", true);
            AbilityDataSO ability = CreateAbilityData("invalid_reward_ability", 2);
            UpgradeDataSO noReward = ScriptableObject.CreateInstance<UpgradeDataSO>();
            UpgradeDataSO doubleReward = CreateUpgradeData(weapon);
            doubleReward.abilityToGrant = ability;
            _createdData.Add(noReward);
            manager.allAvailableUpgrades = new List<UpgradeDataSO> { noReward, doubleReward };

            List<UpgradeDataSO> pool =
                TestObjectUtility.InvokeNonPublicMethod<List<UpgradeDataSO>>(
                    manager,
                    "BuildSelectableUpgradePool");

            Assert.That(pool, Is.Empty);
        }

        /// <summary>能力栏满六种后过滤新能力，但已持有且未满级的能力仍可继续升级。</summary>
        [Test]
        public void BuildSelectableUpgradePool_能力槽已满_只保留已有能力升级()
        {
            GameObject player = CreateTrackedGameObject("AutomationTest_AbilityCapacityPlayer");
            player.AddComponent<PlayerStats>();
            player.AddComponent<PlayerHealth>();
            AbilityManager abilityManager = player.AddComponent<AbilityManager>();
            LevelUpManager manager = CreateManager(player.transform);
            TestObjectUtility.SetPrivateField(manager, "_abilityManager", abilityManager);

            AbilityDataSO firstAbility = null;
            for (int index = 0; index < PlayerLoadoutRules.MaxAbilityCount; index++)
            {
                AbilityDataSO ability = CreateAbilityData($"ability_{index + 1}", index == 0 ? 2 : 1);
                firstAbility = index == 0 ? ability : firstAbility;
                Assert.IsNotNull(abilityManager.GrantOrUpgrade(ability));
            }

            AbilityDataSO seventhAbility = CreateAbilityData("ability_7", 1);
            UpgradeDataSO existingUpgrade = CreateAbilityUpgradeData(firstAbility);
            UpgradeDataSO seventhUpgrade = CreateAbilityUpgradeData(seventhAbility);
            manager.allAvailableUpgrades = new List<UpgradeDataSO>
            {
                seventhUpgrade,
                existingUpgrade
            };

            List<UpgradeDataSO> pool =
                TestObjectUtility.InvokeNonPublicMethod<List<UpgradeDataSO>>(
                    manager,
                    "BuildSelectableUpgradePool");

            CollectionAssert.AreEqual(new[] { existingUpgrade }, pool);
        }

        /// <summary>创建指定数量的场景预置武器，并执行管理器的实际登记逻辑。</summary>
        private LevelUpFixture CreateFixture(int weaponCount, bool firstWeaponCanLevelUp = false)
        {
            GameObject player = CreateTrackedGameObject("AutomationTest_Player");
            var weaponData = new List<WeaponDataSO>(weaponCount);
            var weapons = new List<WeaponBase>(weaponCount);

            for (int index = 0; index < weaponCount; index++)
            {
                WeaponDataSO data = CreateWeaponData(
                    $"weapon_{index + 1}",
                    firstWeaponCanLevelUp && index == 0);
                weaponData.Add(data);
                weapons.Add(CreateWeaponComponent(
                    player.transform,
                    data,
                    $"Weapon_{index + 1}"));
            }

            LevelUpManager manager = CreateManager(player.transform);
            return new LevelUpFixture(manager, weaponData, weapons);
        }

        /// <summary>创建管理器并显式注入玩家，避免 EditMode 测试依赖当前打开场景的 Tag 搜索。</summary>
        private LevelUpManager CreateManager(Transform playerTransform)
        {
            GameObject panel = CreateTrackedGameObject("AutomationTest_LevelUpPanel");
            GameObject managerObject = CreateTrackedGameObject("AutomationTest_LevelUpManager");
            LevelUpManager manager = managerObject.AddComponent<LevelUpManager>();
            manager.levelUpPanel = panel;
            manager.allAvailableUpgrades = new List<UpgradeDataSO>();
            TestObjectUtility.InvokeNonPublicMethod(manager, "Awake");
            TestObjectUtility.SetPrivateField(manager, "playerTransform", playerTransform);
            TestObjectUtility.InvokeNonPublicMethod(manager, "RegisterDefaultWeapons");
            return manager;
        }

        /// <summary>创建带稳定 ID 和可选第二等级的测试武器数据。</summary>
        private WeaponDataSO CreateWeaponData(string weaponId, bool canLevelUp)
        {
            WeaponDataSO data = ScriptableObject.CreateInstance<WeaponDataSO>();
            data.weaponID = weaponId;
            data.weaponNameKey = weaponId + ".name";
            data.levelConfigs = new List<WeaponLevelData>
            {
                new WeaponLevelData()
            };
            if (canLevelUp)
            {
                data.levelConfigs.Add(new WeaponLevelData());
            }

            _createdData.Add(data);
            return data;
        }

        /// <summary>创建仅用于候选池验证的升级数据。</summary>
        private UpgradeDataSO CreateUpgradeData(WeaponDataSO weaponData)
        {
            UpgradeDataSO upgrade = ScriptableObject.CreateInstance<UpgradeDataSO>();
            upgrade.weaponToGrant = weaponData;
            _createdData.Add(upgrade);
            return upgrade;
        }

        /// <summary>创建指定等级数的测试能力数据。</summary>
        private AbilityDataSO CreateAbilityData(string abilityId, int maxLevel)
        {
            AbilityDataSO ability = ScriptableObject.CreateInstance<AbilityDataSO>();
            ability.abilityID = abilityId;
            ability.levelConfigs = new List<AbilityLevelData>();
            for (int level = 0; level < maxLevel; level++)
            {
                ability.levelConfigs.Add(new AbilityLevelData());
            }

            _createdData.Add(ability);
            return ability;
        }

        /// <summary>创建只包含正式能力奖励的合法候选数据。</summary>
        private UpgradeDataSO CreateAbilityUpgradeData(AbilityDataSO abilityData)
        {
            UpgradeDataSO upgrade = ScriptableObject.CreateInstance<UpgradeDataSO>();
            upgrade.abilityToGrant = abilityData;
            _createdData.Add(upgrade);
            return upgrade;
        }

        /// <summary>在玩家子层级创建一项真实 WeaponBase 组件。</summary>
        private WeaponBase CreateWeaponComponent(
            Transform playerTransform,
            WeaponDataSO weaponData,
            string objectName)
        {
            GameObject weaponObject = CreateTrackedGameObject(objectName);
            weaponObject.transform.SetParent(playerTransform, false);
            WeaponBase weapon = weaponObject.AddComponent<WeaponBase>();
            weapon.weaponData = weaponData;
            return weapon;
        }

        /// <summary>集中保存容量测试需要的管理器、数据与运行时武器引用。</summary>
        private sealed class LevelUpFixture
        {
            /// <summary>建立不可变测试夹具。</summary>
            public LevelUpFixture(
                LevelUpManager manager,
                List<WeaponDataSO> weaponData,
                List<WeaponBase> weapons)
            {
                Manager = manager;
                WeaponData = weaponData;
                Weapons = weapons;
            }

            public LevelUpManager Manager { get; }
            public List<WeaponDataSO> WeaponData { get; }
            public List<WeaponBase> Weapons { get; }
        }
    }
}
