using NUnit.Framework;
using UnityEditor;

namespace RainsenVampSur.Tests
{
    /// <summary>验证正式构建从主菜单启动，并保留游戏主场景作为下一跳。</summary>
    public sealed class MainMenuBuildSettingsTests
    {
        private const string MainMenuScenePath = "Assets/Scenes/MainMenu.unity";
        private const string GameplayScenePath = "Assets/Scenes/MainLevel.unity";

        /// <summary>Build Settings 的前两个启用场景应依次为主菜单和游戏主场景。</summary>
        [Test]
        public void BuildSettings_主菜单为首场景_游戏场景紧随其后()
        {
            EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;

            Assert.That(scenes.Length, Is.GreaterThanOrEqualTo(2));
            Assert.IsTrue(scenes[0].enabled);
            Assert.That(scenes[0].path, Is.EqualTo(MainMenuScenePath));
            Assert.IsTrue(scenes[1].enabled);
            Assert.That(scenes[1].path, Is.EqualTo(GameplayScenePath));
        }
    }
}
