using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace RainsenVampSur.Tests.PlayMode
{
    /// <summary>使用正式字体在真实 Player Loop 中验证宝箱横幅初始化及文字网格生成。</summary>
    public sealed class TreasureChestPresentationPlayModeTests : PlayModeComponentTestBase
    {
        /// <summary>配置字体后才启用组件；通过奖励观察者入口验证显示链路，不改变武器授予规则。</summary>
        [UnityTest]
        public IEnumerator Toast_正式字体初始化并生成奖励文字网格()
        {
            Type databaseType = FindLoadedType("UnityEditor.AssetDatabase");
            Type fontType = FindLoadedType("TMPro.TMP_FontAsset");
            MethodInfo load = databaseType.GetMethod("LoadAssetAtPath", new[] { typeof(string), typeof(Type) });
            Assert.IsNotNull(load);
            object font = load.Invoke(null, new object[] { "Assets/Fonts/msyh SDF.asset", fontType });
            Assert.IsNotNull(font);

            GameObject canvasObject = CreateTrackedGameObject("ChestToastFontTest", false);
            canvasObject.AddComponent<RectTransform>();
            canvasObject.AddComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            Component toast = RuntimeComponentTestUtility.AddRuntimeComponent(canvasObject, "TreasureChestRewardToastUI");
            RuntimeComponentTestUtility.SetField(toast, "font", font);
            canvasObject.SetActive(true);
            yield return null;

            // 默认奖励快照保留宝箱标题与等级文本，避免为字体测试创建战斗或武器系统。
            object reward = Activator.CreateInstance(RuntimeComponentTestUtility.RequireRuntimeType("ChestRewardResult"));
            RuntimeComponentTestUtility.Invoke(toast, "HandleChestRewardGranted", reward);
            yield return null;
            object text = RuntimeComponentTestUtility.GetFieldValue<object>(toast, "_rewardText");
            Assert.IsNotNull(text);
            RuntimeComponentTestUtility.Invoke(text, "ForceMeshUpdate", true, true);
            Assert.IsTrue(RuntimeComponentTestUtility.GetProperty<bool>(toast, "IsShowingReward"));
            Assert.AreSame(font, RuntimeComponentTestUtility.GetProperty<object>(text, "font"));
            StringAssert.Contains("宝箱奖励", RuntimeComponentTestUtility.GetProperty<string>(text, "text"));
            object textInfo = RuntimeComponentTestUtility.GetProperty<object>(text, "textInfo");
            Assert.That(RuntimeComponentTestUtility.GetFieldValue<int>(textInfo, "characterCount"), Is.GreaterThan(0));
            Assert.That(RuntimeComponentTestUtility.GetFieldValue<int>(textInfo, "materialCount"), Is.GreaterThan(0));
            LogAssert.NoUnexpectedReceived();
        }

        /// <summary>从已加载程序集中解析编辑器和 TMP 类型，沿用现有测试程序集的反射边界。</summary>
        private static Type FindLoadedType(string fullName)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = assembly.GetType(fullName, false);
                if (type != null) return type;
            }
            throw new TypeLoadException(fullName);
        }
    }
}
