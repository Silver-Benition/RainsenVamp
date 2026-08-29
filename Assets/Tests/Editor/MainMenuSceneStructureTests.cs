using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace RainsenVampSur.Tests
{
    /// <summary>验证主菜单固定 UI 已序列化进场景，且收藏词条不会从重复数据源漂移。</summary>
    public sealed class MainMenuSceneStructureTests
    {
        private const string ScenePath = "Assets/Scenes/MainMenu.unity";
        private const string CatalogPath = "Assets/Data/GameContentCatalog.asset";
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

        /// <summary>收藏入口、按钮布局和收藏固定骨架应在不运行游戏时就存在于 MainMenu 场景。</summary>
        [Test]
        public void MainMenuScene_收藏入口与面板骨架已序列化且按钮位于框体内()
        {
            _openedScene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            RectTransform menuFrame = FindInScene<RectTransform>(_openedScene, "MenuFrame");
            RectTransform buttonGroup = FindInScene<RectTransform>(_openedScene, "ButtonGroup");
            Button startButton = FindInScene<Button>(_openedScene, "StartButton");
            Button collectionButton = FindInScene<Button>(_openedScene, "CollectionButton");
            Button quitButton = FindInScene<Button>(_openedScene, "QuitButton");
            CollectionUI collectionUI = FindComponentInScene<CollectionUI>(_openedScene);
            MainMenuController controller = FindComponentInScene<MainMenuController>(_openedScene);

            Assert.IsNotNull(menuFrame);
            Assert.IsNotNull(buttonGroup);
            Assert.IsNotNull(buttonGroup.GetComponent<VerticalLayoutGroup>());
            Assert.That(buttonGroup.childCount, Is.EqualTo(3));
            Assert.That(startButton.transform.parent, Is.SameAs(buttonGroup));
            Assert.That(collectionButton.transform.parent, Is.SameAs(buttonGroup));
            Assert.That(quitButton.transform.parent, Is.SameAs(buttonGroup));

            LayoutRebuilder.ForceRebuildLayoutImmediate(buttonGroup);
            Canvas.ForceUpdateCanvases();
            AssertRectInside(menuFrame, startButton.GetComponent<RectTransform>());
            AssertRectInside(menuFrame, collectionButton.GetComponent<RectTransform>());
            AssertRectInside(menuFrame, quitButton.GetComponent<RectTransform>());

            Assert.IsNotNull(collectionUI);
            Assert.IsTrue(collectionUI.HasSceneReferences);
            Assert.IsNotNull(collectionUI.PanelRoot);
            Assert.IsFalse(collectionUI.PanelRoot.gameObject.activeSelf);
            Assert.IsNotNull(collectionUI.PanelRoot.Find("CharacterTab"));
            Assert.IsNotNull(collectionUI.PanelRoot.Find("WeaponTab"));
            Assert.IsNotNull(collectionUI.PanelRoot.Find("UpgradeTab"));
            Assert.IsNotNull(collectionUI.PanelRoot.Find("CollectionContent"));
            Assert.IsNotNull(collectionUI.PanelRoot.Find("CollectionBackButton"));

            SerializedObject serializedController = new SerializedObject(controller);
            Assert.That(
                serializedController.FindProperty("collectionButton").objectReferenceValue,
                Is.SameAs(collectionButton));
            Assert.That(
                serializedController.FindProperty("collectionUI").objectReferenceValue,
                Is.SameAs(collectionUI));
        }

        /// <summary>所有武器型升级在收藏页都应复用对应 WeaponDataSO 的权威名称与描述。</summary>
        [Test]
        public void Collection_武器型升级_复用武器权威词条()
        {
            GameContentCatalogSO catalog = AssetDatabase.LoadAssetAtPath<GameContentCatalogSO>(
                CatalogPath);
            MethodInfo getName = typeof(CollectionUI).GetMethod(
                "GetUpgradeCollectionName",
                BindingFlags.Static | BindingFlags.NonPublic);
            MethodInfo getDescription = typeof(CollectionUI).GetMethod(
                "GetUpgradeCollectionDescription",
                BindingFlags.Static | BindingFlags.NonPublic);

            Assert.IsNotNull(catalog);
            Assert.IsNotNull(getName);
            Assert.IsNotNull(getDescription);
            for (int index = 0; index < catalog.Upgrades.Count; index++)
            {
                UpgradeDataSO upgrade = catalog.Upgrades[index];
                if (upgrade == null || upgrade.weaponToGrant == null)
                {
                    continue;
                }

                Assert.That(
                    getName.Invoke(null, new object[] { upgrade }),
                    Is.EqualTo(upgrade.weaponToGrant.GetDisplayName()),
                    upgrade.name);
                Assert.That(
                    getDescription.Invoke(null, new object[] { upgrade }),
                    Is.EqualTo(upgrade.weaponToGrant.GetDisplayDescription()),
                    upgrade.name);
            }
        }

        /// <summary>在包含未启用节点的场景层级中按名称寻找指定组件。</summary>
        private static T FindInScene<T>(Scene scene, string objectName) where T : Component
        {
            GameObject[] roots = scene.GetRootGameObjects();
            for (int rootIndex = 0; rootIndex < roots.Length; rootIndex++)
            {
                Transform[] transforms = roots[rootIndex].GetComponentsInChildren<Transform>(true);
                for (int index = 0; index < transforms.Length; index++)
                {
                    if (transforms[index].name == objectName &&
                        transforms[index].TryGetComponent(out T component))
                    {
                        return component;
                    }
                }
            }

            return null;
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

        /// <summary>断言子矩形四角都位于主菜单框体本地边界内。</summary>
        private static void AssertRectInside(RectTransform container, RectTransform child)
        {
            Vector3[] corners = new Vector3[4];
            child.GetWorldCorners(corners);
            for (int index = 0; index < corners.Length; index++)
            {
                Vector3 local = container.InverseTransformPoint(corners[index]);
                Assert.That(local.x, Is.InRange(container.rect.xMin, container.rect.xMax), child.name);
                Assert.That(local.y, Is.InRange(container.rect.yMin, container.rect.yMax), child.name);
            }
        }
    }
}
