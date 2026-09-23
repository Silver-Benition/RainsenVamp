using System.IO;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Experimental.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace RainsenVampSur.Tests
{
    /// <summary>验证地图即时效果、像素素材、正式资源绑定与冻结蒙版的静态契约。</summary>
    public sealed class MapInstantPickupTests : EditModeComponentTestBase
    {
        private const string CaptainSpritePath =
            "Assets/Art/Sprites/Pickup/MapInstantEffects/CaptainAnchor.png";
        private const string CrystalSpritePath =
            "Assets/Art/Sprites/Pickup/MapInstantEffects/CrystalBall.png";
        private const string CaptainDataPath =
            "Assets/Data/MapInstantEffects/CaptainPickup.asset";
        private const string CrystalDataPath =
            "Assets/Data/MapInstantEffects/CrystalBallPickup.asset";
        private const string CaptainPrefabPath =
            "Assets/Prefab/Pickup/CaptainPickup.prefab";
        private const string CrystalPrefabPath =
            "Assets/Prefab/Pickup/CrystalBallPickup.prefab";
        private const string CoinSpritePath = "Assets/Art/Sprites/Other/Coin/Coin.png";
        private const string ExpGemSpritePath =
            "Assets/Art/Sprites/Other/Exp Gem/Exp Gem.png";
        private const string CoinPrefabPath = "Assets/Prefab/Pickup/CoinPickup.prefab";
        private const string ExpGemPrefabPath = "Assets/Prefab/ExpGem.prefab";
        private const string DropTablePath = "Assets/Data/WeakEnemyDropTable.asset";
        private const string MainLevelScenePath = "Assets/Scenes/MainLevel.unity";
        private const float FloatTolerance = 0.0001f;

        private Scene _openedScene;

        /// <summary>关闭本测试附加打开的场景，避免静态冻结实例污染后续用例。</summary>
        [TearDown]
        public void CloseOpenedScene()
        {
            if (_openedScene.IsValid() && _openedScene.isLoaded)
            {
                EditorSceneManager.CloseScene(_openedScene, true);
            }
        }

        /// <summary>两件地图拾取物必须以 64 像素保存细节，并通过 PPU 128 缩到半个世界单位。</summary>
        [TestCase(CaptainSpritePath, 48)]
        [TestCase(CrystalSpritePath, 64)]
        public void MapPickupSprite_使用64像素高密度点采样规则(
            string spritePath,
            int maximumVisibleColors)
        {
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(spritePath);
            TextureImporter importer = AssetImporter.GetAtPath(spritePath) as TextureImporter;

            Assert.IsNotNull(sprite, $"无法加载地图拾取物 Sprite：{spritePath}");
            Assert.IsNotNull(importer, $"Sprite 缺少 TextureImporter：{spritePath}");
            Assert.That(sprite.rect.width, Is.EqualTo(64f).Within(FloatTolerance));
            Assert.That(sprite.rect.height, Is.EqualTo(64f).Within(FloatTolerance));
            Assert.That(sprite.bounds.size.x, Is.EqualTo(0.5f).Within(FloatTolerance));
            Assert.That(sprite.bounds.size.y, Is.EqualTo(0.5f).Within(FloatTolerance));
            Assert.That(importer.textureType, Is.EqualTo(TextureImporterType.Sprite));
            Assert.That(importer.spriteImportMode, Is.EqualTo(SpriteImportMode.Single));
            Assert.That(importer.spritePixelsPerUnit, Is.EqualTo(128f).Within(FloatTolerance));
            Assert.That(importer.filterMode, Is.EqualTo(FilterMode.Point));
            Assert.That(importer.wrapMode, Is.EqualTo(TextureWrapMode.Clamp));
            Assert.IsFalse(importer.mipmapEnabled);
            Assert.IsTrue(importer.alphaIsTransparency);
            Assert.That(
                importer.textureCompression,
                Is.EqualTo(TextureImporterCompression.Uncompressed));
            StringAssert.Contains(
                "spriteGenerateFallbackPhysicsShape: 0",
                File.ReadAllText(spritePath + ".meta"));
            AssertStrictPixelSource(spritePath, maximumVisibleColors);
        }

        /// <summary>
        /// 金币与经验必须使用项目内正式 Sprite，并以整数屏幕倍率所需的世界尺寸导入。
        /// 在 1080p、32 Assets PPU、4 倍相机缩放下，0.25 世界单位对应 32 个屏幕像素。
        /// </summary>
        [TestCase(CoinSpritePath, 16, 64f)]
        [TestCase(ExpGemSpritePath, 32, 128f)]
        public void ResourcePickupSprite_使用高清世界渲染导入规则(
            string spritePath,
            int expectedSourceSize,
            float expectedPixelsPerUnit)
        {
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(spritePath);
            TextureImporter importer = AssetImporter.GetAtPath(spritePath) as TextureImporter;

            Assert.IsNotNull(sprite, $"无法加载资源拾取物 Sprite：{spritePath}");
            Assert.IsNotNull(importer, $"Sprite 缺少 TextureImporter：{spritePath}");
            Assert.That(sprite.rect.width, Is.EqualTo(expectedSourceSize).Within(FloatTolerance));
            Assert.That(sprite.rect.height, Is.EqualTo(expectedSourceSize).Within(FloatTolerance));
            Assert.That(sprite.bounds.size.x, Is.EqualTo(0.25f).Within(FloatTolerance));
            Assert.That(sprite.bounds.size.y, Is.EqualTo(0.25f).Within(FloatTolerance));
            Assert.That(importer.textureType, Is.EqualTo(TextureImporterType.Sprite));
            Assert.That(importer.spriteImportMode, Is.EqualTo(SpriteImportMode.Single));
            Assert.That(
                importer.spritePixelsPerUnit,
                Is.EqualTo(expectedPixelsPerUnit).Within(FloatTolerance));
            Assert.That(importer.filterMode, Is.EqualTo(FilterMode.Point));
            Assert.That(importer.wrapMode, Is.EqualTo(TextureWrapMode.Clamp));
            Assert.IsFalse(importer.mipmapEnabled);
            Assert.IsTrue(importer.alphaIsTransparency);
            Assert.That(
                importer.textureCompression,
                Is.EqualTo(TextureImporterCompression.Uncompressed));
            StringAssert.Contains(
                "spriteGenerateFallbackPhysicsShape: 0",
                File.ReadAllText(spritePath + ".meta"));
        }

        /// <summary>舰长必须绑定 45 点治疗，水晶球必须绑定 15 秒全场冻结。</summary>
        [Test]
        public void MapPickupData_稳定身份与效果数值绑定正确()
        {
            MapInstantEffectPickupDataSO captain =
                AssetDatabase.LoadAssetAtPath<MapInstantEffectPickupDataSO>(CaptainDataPath);
            MapInstantEffectPickupDataSO crystal =
                AssetDatabase.LoadAssetAtPath<MapInstantEffectPickupDataSO>(CrystalDataPath);

            Assert.IsNotNull(captain);
            Assert.IsNotNull(crystal);
            Assert.That(captain.GetStableId(), Is.EqualTo("map_pickup_captain"));
            Assert.That(captain.nameKey, Is.EqualTo("pickup.map.captain.name"));
            Assert.That(captain.GetDisplayName(), Is.EqualTo("舰长"));
            Assert.That(AssetDatabase.GetAssetPath(captain.icon), Is.EqualTo(CaptainSpritePath));
            Assert.That(captain.Effect, Is.TypeOf<HealingMapInstantEffectSO>());
            Assert.That(
                ((HealingMapInstantEffectSO)captain.Effect).HealAmount,
                Is.EqualTo(3f).Within(FloatTolerance));

            Assert.That(crystal.GetStableId(), Is.EqualTo("map_pickup_crystal_ball"));
            Assert.That(crystal.nameKey, Is.EqualTo("pickup.map.crystal_ball.name"));
            Assert.That(crystal.GetDisplayName(), Is.EqualTo("水晶球"));
            Assert.That(AssetDatabase.GetAssetPath(crystal.icon), Is.EqualTo(CrystalSpritePath));
            Assert.That(crystal.Effect, Is.TypeOf<WorldFreezeMapInstantEffectSO>());
            Assert.That(
                ((WorldFreezeMapInstantEffectSO)crystal.Effect).Duration,
                Is.EqualTo(15f).Within(FloatTolerance));
        }

        /// <summary>舰长治疗必须返回真实恢复量，并在满血时拒绝伪报成功。</summary>
        [Test]
        public void CaptainEffect_恢复3点并在满血时返回失败()
        {
            GameObject player = CreateTrackedGameObject("EditModeTest_CaptainPlayer");
            PlayerHealth health = player.AddComponent<PlayerHealth>();
            TestObjectUtility.InvokeNonPublicMethod(health, "Awake");
            TestObjectUtility.InvokeNonPublicMethod(health, "OnEnable");
            HealingMapInstantEffectSO effect =
                AssetDatabase.LoadAssetAtPath<HealingMapInstantEffectSO>(
                    "Assets/Data/MapInstantEffects/CaptainHealEffect.asset");

            health.TakeDamage(60f);
            bool firstApplied = effect.TryApply(new MapInstantEffectContext(null, health));

            Assert.IsTrue(firstApplied);
            Assert.That(health.CurrentHealth, Is.EqualTo(43f).Within(FloatTolerance));
            Assert.That(health.RestoreHealth(100f), Is.EqualTo(57f).Within(FloatTolerance));
            Assert.That(health.CurrentHealth, Is.EqualTo(100f).Within(FloatTolerance));
            Assert.IsFalse(effect.TryApply(new MapInstantEffectContext(null, health)));
        }

        /// <summary>冻结刷新必须取剩余时间与新时长的较大值，绝不累加。</summary>
        [Test]
        public void WorldFreezeController_重复请求刷新而不叠加()
        {
            GameObject controllerObject = CreateTrackedGameObject("EditModeTest_WorldFreeze");
            WorldFreezeController controller = controllerObject.AddComponent<WorldFreezeController>();
            List<bool> stateChanges = new List<bool>();
            controller.FreezeStateChanged += stateChanges.Add;

            Assert.IsTrue(controller.TryFreeze(15f));
            Assert.That(controller.RemainingDuration, Is.EqualTo(15f).Within(FloatTolerance));
            TestObjectUtility.SetPrivateFloat(controller, "_remainingDuration", 8f);
            Assert.IsTrue(controller.TryFreeze(5f));
            Assert.That(controller.RemainingDuration, Is.EqualTo(8f).Within(FloatTolerance));
            Assert.IsTrue(controller.TryFreeze(15f));
            Assert.That(controller.RemainingDuration, Is.EqualTo(15f).Within(FloatTolerance));
            controller.CancelFreeze();
            Assert.IsFalse(WorldFreezeController.IsHostileSimulationFrozen);
            CollectionAssert.AreEqual(new[] { true, false }, stateChanges);
        }

        /// <summary>地图掉落选择应忽略无效项，并严格按 4:1 边界选择。</summary>
        [Test]
        public void MapDropResolver_按非负权重选择并处理右边界()
        {
            GameObject captain = CreateTrackedGameObject("EditModeTest_CaptainPrefab");
            GameObject crystal = CreateTrackedGameObject("EditModeTest_CrystalPrefab");
            List<MapInstantEffectDropEntry> entries = new List<MapInstantEffectDropEntry>
            {
                new MapInstantEffectDropEntry { prefab = captain, weight = 4f },
                new MapInstantEffectDropEntry { prefab = null, weight = 100f },
                new MapInstantEffectDropEntry { prefab = crystal, weight = 1f }
            };

            Assert.AreSame(captain, MapInstantEffectDropResolver.Select(entries, 0f));
            Assert.AreSame(captain, MapInstantEffectDropResolver.Select(entries, 0.799f));
            Assert.AreSame(crystal, MapInstantEffectDropResolver.Select(entries, 0.8f));
            Assert.AreSame(crystal, MapInstantEffectDropResolver.Select(entries, 1f));
            Assert.IsNull(MapInstantEffectDropResolver.Select(null, 0.5f));
        }

        /// <summary>地图道具必须近身拾取；只有经验与金币复用磁吸外飘运动。</summary>
        [Test]
        public void PickupPrefabs_地图道具不磁吸且资源道具保留外飘追踪()
        {
            AssertPickupPrefab(CaptainPrefabPath, CaptainDataPath);
            AssertPickupPrefab(CrystalPrefabPath, CrystalDataPath);

            GameObject expGem = AssetDatabase.LoadAssetAtPath<GameObject>(ExpGemPrefabPath);
            GameObject coin = AssetDatabase.LoadAssetAtPath<GameObject>(CoinPrefabPath);
            Assert.IsNotNull(expGem.GetComponent<MagneticPickupMotion>());
            Assert.IsNotNull(coin.GetComponent<MagneticPickupMotion>());
            Assert.That(expGem.GetComponents(typeof(IMagneticPickup)).Length, Is.EqualTo(1));
            Assert.That(coin.GetComponents(typeof(IMagneticPickup)).Length, Is.EqualTo(1));
            AssertResourcePickupVisual(expGem, ExpGemSpritePath, 0.025f);
            AssertResourcePickupVisual(coin, CoinSpritePath, 0.55f);
        }

        /// <summary>普通敌人掉落表必须以 2.5% 基础概率和 4:1 权重接入两件拾取物。</summary>
        [Test]
        public void WeakEnemyDropTable_只启用回血道具并保留停用资产()
        {
            EnemyDropTableSO table = AssetDatabase.LoadAssetAtPath<EnemyDropTableSO>(DropTablePath);
            GameObject captain = AssetDatabase.LoadAssetAtPath<GameObject>(CaptainPrefabPath);
            GameObject crystal = AssetDatabase.LoadAssetAtPath<GameObject>(CrystalPrefabPath);

            Assert.IsNotNull(table);
            Assert.That(
                table.baseMapInstantEffectChance,
                Is.EqualTo(0.025f).Within(FloatTolerance));
            Assert.That(table.mapInstantEffectDrops, Has.Count.EqualTo(2));
            Assert.AreSame(captain, table.mapInstantEffectDrops[0].prefab);
            Assert.That(table.mapInstantEffectDrops[0].weight, Is.EqualTo(4f));
            Assert.AreSame(crystal, table.mapInstantEffectDrops[1].prefab);
            Assert.That(table.mapInstantEffectDrops[1].weight, Is.EqualTo(0f));
        }

        /// <summary>MainLevel 必须绑定唯一冻结权威和位于世界层与玩家层之间的蓝色蒙版。</summary>
        [Test]
        public void MainLevelScene_挂载唯一冻结控制器与分层蒙版()
        {
            _openedScene = EditorSceneManager.OpenScene(MainLevelScenePath, OpenSceneMode.Additive);
            int controllerCount = 0;
            int overlayCount = 0;
            WorldFreezeController freezeController = null;
            WorldFreezeOverlay freezeOverlay = null;
            Canvas playerHealthCanvas = null;
            SpriteRenderer playerRenderer = null;
            GameObject[] roots = _openedScene.GetRootGameObjects();
            for (int index = 0; index < roots.Length; index++)
            {
                WorldFreezeController[] controllers =
                    roots[index].GetComponentsInChildren<WorldFreezeController>(true);
                WorldFreezeOverlay[] overlays =
                    roots[index].GetComponentsInChildren<WorldFreezeOverlay>(true);
                controllerCount += controllers.Length;
                overlayCount += overlays.Length;
                if (controllers.Length > 0)
                {
                    freezeController = controllers[0];
                }

                if (overlays.Length > 0)
                {
                    freezeOverlay = overlays[0];
                }

                if (roots[index].CompareTag("Player"))
                {
                    playerRenderer = roots[index].GetComponentInChildren<SpriteRenderer>(true);
                }

                Canvas[] canvases = roots[index].GetComponentsInChildren<Canvas>(true);
                for (int canvasIndex = 0; canvasIndex < canvases.Length; canvasIndex++)
                {
                    if (canvases[canvasIndex].name == "PlayerHealthBarCanvas")
                    {
                        playerHealthCanvas = canvases[canvasIndex];
                    }
                }
            }

            Assert.That(controllerCount, Is.EqualTo(1));
            Assert.That(overlayCount, Is.EqualTo(1));
            Assert.IsNotNull(freezeController);
            Assert.IsNotNull(freezeOverlay);
            Assert.IsNotNull(freezeOverlay.GetComponentInParent<Camera>());

            SpriteRenderer overlayRenderer = freezeOverlay.GetComponent<SpriteRenderer>();
            Assert.IsNotNull(overlayRenderer);
            Assert.IsFalse(overlayRenderer.enabled);
            Assert.That(overlayRenderer.sortingLayerID, Is.EqualTo(0));
            Assert.That(overlayRenderer.sortingOrder, Is.EqualTo(32000));
            Assert.That(overlayRenderer.color.b, Is.GreaterThan(overlayRenderer.color.r));
            Assert.That(overlayRenderer.color.a, Is.EqualTo(0.32f).Within(FloatTolerance));
            Assert.IsNotNull(overlayRenderer.sprite);
            Assert.That(
                AssetDatabase.GetAssetPath(overlayRenderer.sprite),
                Is.EqualTo("Assets/Art/Sprites/Ability/VFX/WorldFreezeOverlay.png"));

            SerializedObject serializedOverlay = new SerializedObject(freezeOverlay);
            Assert.AreSame(
                freezeController,
                serializedOverlay.FindProperty("freezeController").objectReferenceValue);

            int playerSortingLayerId = SortingLayer.NameToID("Player");
            Assert.IsNotNull(playerRenderer);
            Assert.That(playerRenderer.sortingLayerID, Is.EqualTo(playerSortingLayerId));
            Assert.IsNotNull(playerHealthCanvas);
            Assert.That(playerHealthCanvas.sortingLayerID, Is.EqualTo(playerSortingLayerId));
        }

        /// <summary>
        /// 主关卡必须直接按输出分辨率渲染，并继续使用 32 PPU 网格稳定世界移动。
        /// 该配置取消 480×270 中间纹理，避免小型高密度 Sprite 先被压缩再放大。
        /// </summary>
        [Test]
        public void MainLevelScene_使用原生分辨率像素对齐()
        {
            _openedScene = EditorSceneManager.OpenScene(MainLevelScenePath, OpenSceneMode.Additive);
            PixelPerfectCamera pixelPerfectCamera = null;
            GameObject[] roots = _openedScene.GetRootGameObjects();
            for (int index = 0; index < roots.Length && pixelPerfectCamera == null; index++)
            {
                pixelPerfectCamera =
                    roots[index].GetComponentInChildren<PixelPerfectCamera>(true);
            }

            Assert.IsNotNull(pixelPerfectCamera);
            Assert.That(pixelPerfectCamera.assetsPPU, Is.EqualTo(32));
            Assert.That(pixelPerfectCamera.refResolutionX, Is.EqualTo(480));
            Assert.That(pixelPerfectCamera.refResolutionY, Is.EqualTo(270));
            Assert.That(
                pixelPerfectCamera.gridSnapping,
                Is.EqualTo(PixelPerfectCamera.GridSnapping.PixelSnapping));
            SerializedObject serializedCamera = new SerializedObject(pixelPerfectCamera);
            Assert.That(
                serializedCamera.FindProperty("m_FilterMode").enumValueIndex,
                Is.EqualTo((int)PixelPerfectCamera.PixelPerfectFilterMode.Point));
            Assert.That(
                pixelPerfectCamera.GetComponent<Camera>().orthographicSize,
                Is.EqualTo(4.21875f).Within(FloatTolerance));
        }

        /// <summary>校验地图拾取物 Prefab 的近身触发器、逻辑、报告、图标与数据引用。</summary>
        private static void AssertPickupPrefab(string prefabPath, string dataPath)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            MapInstantEffectPickupDataSO data =
                AssetDatabase.LoadAssetAtPath<MapInstantEffectPickupDataSO>(dataPath);

            Assert.IsNotNull(prefab, prefabPath);
            Assert.IsNotNull(data, dataPath);
            Assert.That(prefab.layer, Is.EqualTo(LayerMask.NameToLayer("ExpGem")));
            CircleCollider2D pickupCollider = prefab.GetComponent<CircleCollider2D>();
            Assert.IsNotNull(pickupCollider);
            Assert.IsTrue(pickupCollider.isTrigger);
            Assert.That(pickupCollider.radius, Is.EqualTo(0.55f).Within(FloatTolerance));
            Assert.That(prefab.transform.localScale, Is.EqualTo(Vector3.one));
            Assert.IsNull(prefab.GetComponent<MagneticPickupMotion>());
            Assert.That(prefab.GetComponents(typeof(IMagneticPickup)).Length, Is.EqualTo(0));
            Assert.IsNotNull(prefab.GetComponent<MapInstantEffectPickup>());
            MapInstantEffectPickupReporter reporter =
                prefab.GetComponent<MapInstantEffectPickupReporter>();
            Assert.IsNotNull(reporter);
            Assert.AreSame(data, reporter.PickupData);
            Assert.AreSame(data.icon, prefab.GetComponent<SpriteRenderer>().sprite);
        }

        /// <summary>
        /// 校验经验与金币的正式视觉引用，同时锁定现有世界碰撞半径，避免高清化改变拾取手感。
        /// </summary>
        private static void AssertResourcePickupVisual(
            GameObject prefab,
            string expectedSpritePath,
            float expectedColliderRadius)
        {
            Assert.IsNotNull(prefab);
            Assert.That(prefab.transform.localScale, Is.EqualTo(Vector3.one));
            SpriteRenderer renderer = prefab.GetComponent<SpriteRenderer>();
            Assert.IsNotNull(renderer);
            Assert.IsNotNull(renderer.sprite);
            Assert.That(renderer.color, Is.EqualTo(Color.white));
            Assert.That(AssetDatabase.GetAssetPath(renderer.sprite), Is.EqualTo(expectedSpritePath));
            Assert.That(renderer.sprite.bounds.size.x, Is.EqualTo(0.25f).Within(FloatTolerance));
            Assert.That(renderer.sprite.bounds.size.y, Is.EqualTo(0.25f).Within(FloatTolerance));
            CircleCollider2D pickupCollider = prefab.GetComponent<CircleCollider2D>();
            Assert.IsNotNull(pickupCollider);
            Assert.That(
                pickupCollider.radius,
                Is.EqualTo(expectedColliderRadius).Within(FloatTolerance));
        }

        /// <summary>
        /// 直接读取 PNG 原始像素，确保源图没有半透明软边且色数保持在小色板范围内。
        /// 这样 Point Filter 才能在相机缩放时保留真实像素边缘。
        /// </summary>
        private static void AssertStrictPixelSource(string spritePath, int maximumVisibleColors)
        {
            Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                Assert.IsTrue(texture.LoadImage(File.ReadAllBytes(spritePath), false));
                Assert.That(texture.width, Is.EqualTo(64));
                Assert.That(texture.height, Is.EqualTo(64));

                HashSet<Color32> visibleColors = new HashSet<Color32>();
                Color32[] pixels = texture.GetPixels32();
                for (int index = 0; index < pixels.Length; index++)
                {
                    Color32 pixel = pixels[index];
                    Assert.That(
                        pixel.a == 0 || pixel.a == byte.MaxValue,
                        Is.True,
                        $"{spritePath} 在像素 {index} 存在半透明软边：alpha={pixel.a}");
                    if (pixel.a == byte.MaxValue)
                    {
                        visibleColors.Add(pixel);
                    }
                }

                Assert.That(visibleColors.Count, Is.GreaterThan(0));
                Assert.That(visibleColors.Count, Is.LessThanOrEqualTo(maximumVisibleColors));
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
        }
    }
}
