using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RainsenVampSur.Tests
{
    /// <summary>验证宝箱正式素材、池化开启表现与主 HUD 奖励提示的序列化契约。</summary>
    public sealed class TreasureChestPresentationTests
    {
        private const string ChestSpritePath =
            "Assets/Art/Sprites/Pickup/TreasureChest.png";
        private const string BurstSpritePath =
            "Assets/Art/Sprites/Pickup/TreasureChestBurst.png";
        private const string ChestPrefabPath =
            "Assets/Prefab/Pickup/TreasureChestPickup.prefab";
        private const string BurstPrefabPath =
            "Assets/Prefab/VFX/TreasureChestOpenVfx.prefab";
        private const string MainLevelScenePath = "Assets/Scenes/MainLevel.unity";
        private const float FloatTolerance = 0.0001f;

        private Scene _openedScene;

        /// <summary>每项场景测试结束后关闭附加场景，避免污染其他 EditMode 用例。</summary>
        [TearDown]
        public void CloseOpenedScene()
        {
            if (_openedScene.IsValid() && _openedScene.isLoaded)
            {
                EditorSceneManager.CloseScene(_openedScene, true);
            }
        }

        /// <summary>宝箱和开启爆闪必须使用统一的 48 像素点采样导入规则。</summary>
        [TestCase(ChestSpritePath)]
        [TestCase(BurstSpritePath)]
        public void TreasureChestSprite_使用像素项目正式导入规则(string spritePath)
        {
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(spritePath);
            TextureImporter importer = AssetImporter.GetAtPath(spritePath) as TextureImporter;

            Assert.IsNotNull(sprite, $"无法加载宝箱 Sprite：{spritePath}");
            Assert.IsNotNull(importer, $"Sprite 缺少 TextureImporter：{spritePath}");
            Assert.That(sprite.rect.width, Is.EqualTo(48f).Within(FloatTolerance));
            Assert.That(sprite.rect.height, Is.EqualTo(48f).Within(FloatTolerance));
            Assert.That(importer.textureType, Is.EqualTo(TextureImporterType.Sprite));
            Assert.That(importer.spriteImportMode, Is.EqualTo(SpriteImportMode.Single));
            Assert.That(importer.spritePixelsPerUnit, Is.EqualTo(48f).Within(FloatTolerance));
            Assert.That(importer.filterMode, Is.EqualTo(FilterMode.Point));
            Assert.That(importer.wrapMode, Is.EqualTo(TextureWrapMode.Clamp));
            Assert.IsFalse(importer.mipmapEnabled);
            Assert.IsTrue(importer.alphaIsTransparency);
            Assert.That(
                importer.textureCompression,
                Is.EqualTo(TextureImporterCompression.Uncompressed));

            string metaText = File.ReadAllText(spritePath + ".meta");
            StringAssert.Contains(
                "spriteGenerateFallbackPhysicsShape: 0",
                metaText,
                $"宝箱表现 Sprite 不应生成无用途的物理轮廓：{spritePath}");
        }

        /// <summary>宝箱 Prefab 必须显示正式宝箱、保留拾取保护并引用池化开启特效。</summary>
        [Test]
        public void TreasureChestPrefab_正式表现与拾取保护引用完整()
        {
            GameObject chestPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ChestPrefabPath);
            GameObject burstPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BurstPrefabPath);

            Assert.IsNotNull(chestPrefab);
            Assert.IsNotNull(burstPrefab);
            Assert.That(chestPrefab.name, Is.EqualTo("TreasureChestPickup"));

            SpriteRenderer spriteRenderer = chestPrefab.GetComponent<SpriteRenderer>();
            CircleCollider2D pickupCollider = chestPrefab.GetComponent<CircleCollider2D>();
            TreasureChestPickup pickup = chestPrefab.GetComponent<TreasureChestPickup>();
            Assert.IsNotNull(spriteRenderer);
            Assert.IsNotNull(pickupCollider);
            Assert.IsNotNull(pickup);
            Assert.That(
                AssetDatabase.GetAssetPath(spriteRenderer.sprite),
                Is.EqualTo(ChestSpritePath));
            Assert.That(spriteRenderer.color, Is.EqualTo(Color.white));
            Assert.That(spriteRenderer.sortingOrder, Is.EqualTo(4));
            Assert.IsTrue(pickupCollider.isTrigger);
            Assert.That(pickupCollider.radius, Is.EqualTo(0.5f).Within(FloatTolerance));

            SerializedObject serializedPickup = new SerializedObject(pickup);
            Assert.That(
                serializedPickup.FindProperty("pickupProtectionDuration").floatValue,
                Is.EqualTo(0.5f).Within(FloatTolerance));
            Assert.That(
                serializedPickup.FindProperty("openVfxPrefab").objectReferenceValue,
                Is.SameAs(burstPrefab));
        }

        /// <summary>开启表现 Prefab 必须使用正式爆闪 Sprite 与可回池播放组件。</summary>
        [Test]
        public void TreasureChestBurstPrefab_池化播放组件与Sprite引用完整()
        {
            GameObject burstPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BurstPrefabPath);

            Assert.IsNotNull(burstPrefab);
            SpriteRenderer spriteRenderer = burstPrefab.GetComponent<SpriteRenderer>();
            PooledSpriteBurstVfx burstVfx = burstPrefab.GetComponent<PooledSpriteBurstVfx>();
            Assert.IsNotNull(spriteRenderer);
            Assert.IsNotNull(burstVfx);
            Assert.That(
                AssetDatabase.GetAssetPath(spriteRenderer.sprite),
                Is.EqualTo(BurstSpritePath));
            Assert.That(spriteRenderer.sortingOrder, Is.EqualTo(10));
        }

        /// <summary>MainLevel 主 Canvas 必须序列化宝箱奖励横幅，并保留正式字体与本地化键。</summary>
        [Test]
        public void MainLevelScene_主Canvas挂载宝箱奖励提示()
        {
            _openedScene = EditorSceneManager.OpenScene(MainLevelScenePath, OpenSceneMode.Additive);
            TreasureChestRewardToastUI toast = FindComponentInScene<TreasureChestRewardToastUI>(
                _openedScene);

            Assert.IsNotNull(toast);
            Assert.IsNotNull(toast.GetComponent<Canvas>());
            SerializedObject serializedToast = new SerializedObject(toast);
            Assert.That(
                serializedToast.FindProperty("rewardTitleKey").stringValue,
                Is.EqualTo("ui.treasure.reward"));
            Assert.IsNotNull(serializedToast.FindProperty("font").objectReferenceValue);
            Assert.That(
                serializedToast.FindProperty("maxQueuedRewards").intValue,
                Is.EqualTo(8));
        }

        /// <summary>在包含未启用节点的场景层级中寻找首个指定组件。</summary>
        private static T FindComponentInScene<T>(Scene scene) where T : Component
        {
            GameObject[] roots = scene.GetRootGameObjects();
            for (int index = 0; index < roots.Length; index++)
            {
                T component = roots[index].GetComponentInChildren<T>(true);
                if (component != null)
                {
                    return component;
                }
            }

            return null;
        }
    }
}
