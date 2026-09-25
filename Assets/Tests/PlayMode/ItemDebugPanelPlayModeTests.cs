using System;
using System.Collections;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace RainsenVampSur.Tests.PlayMode
{
    /// <summary>在正式场景验证 F7 相同开关入口、真实 OnGUI 重绘、获取与跨场景引用刷新。</summary>
    public sealed class ItemDebugPanelPlayModeTests
    {
        private Component _panel;

        /// <summary>使用内存账号加载正式场景，避免写入用户存档。</summary>
        [UnitySetUp] public IEnumerator Setup()
        {
            ResetAccount();yield return SceneManager.LoadSceneAsync("MainLevel");
            for(int i=0;i<6;i++) yield return null;
            _panel=(Component)UnityEngine.Object.FindObjectOfType(TypeOf("AbilityDebugPanel"));
            if(_panel==null)
            {
                var root=new GameObject("DebugPanelTest");UnityEngine.Object.DontDestroyOnLoad(root);
                _panel=root.AddComponent(TypeOf("AbilityDebugPanel"));
            }
            if((bool)Field(_panel,"_visible")) Call(_panel,"TogglePanel");
        }

        /// <summary>结束时销毁开发窗口，卸载测试场景并释放内存账号。</summary>
        [UnityTearDown] public IEnumerator Cleanup()
        {
            if(_panel!=null) UnityEngine.Object.Destroy(_panel.gameObject);
            Time.timeScale=1;var empty=SceneManager.CreateScene("F7Empty");SceneManager.SetActiveScene(empty);
            var main=SceneManager.GetSceneByName("MainLevel");if(main.isLoaded)yield return SceneManager.UnloadSceneAsync(main);
            ResetAccount();
        }

        /// <summary>打开含无限叠加道具的窗口，多帧重绘保持可用；批量获取遵守上限且可再次关闭。</summary>
        [UnityTest] public IEnumerator OpenAndGrantItemsWithoutEnumeratingStackLimit()
        {
            Call(_panel,"TogglePanel");
            for(int i=0;i<10;i++) yield return null;
            Assert.IsTrue((bool)Field(_panel,"_visible"));
            IList items=(IList)Field(_panel,"_items");Assert.AreEqual(24,items.Count);
            int reviewed=0;
            foreach(object item in items)
            {
                Assert.AreEqual("Item",item.GetType().GetField("presentationCategory").GetValue(item).ToString());
                if((bool)item.GetType().GetField("stackPerCopy").GetValue(item)) reviewed++;
            }
            Assert.AreEqual(20,reviewed);
            object bandage=FindItem(items,"moss_bandage"),gear=FindItem(items,"red_gear"),clover=FindItem(items,"clover_coin");
            Assert.AreEqual(5,Call(_panel,"GrantItems",bandage,int.MaxValue));
            Assert.AreEqual(3,Call(_panel,"GrantItems",gear,5));
            Assert.AreEqual(1,Call(_panel,"GrantItems",clover,5));
            Assert.AreEqual(0,Call(_panel,"GrantItems",clover,1));
            yield return Capture();
            Call(_panel,"TogglePanel");Assert.IsFalse((bool)Field(_panel,"_visible"));
        }

        /// <summary>场景重载后重新打开必须绑定新角色，不能继续使用上一局持有状态。</summary>
        [UnityTest] public IEnumerator ReopenAfterReloadUsesNewPlayer()
        {
            Call(_panel,"TogglePanel");object oldManager=Field(_panel,"_abilityManager");
            Call(_panel,"GrantItems",FindItem((IList)Field(_panel,"_items"),"clover_coin"),1);
            Call(_panel,"TogglePanel");
            yield return SceneManager.LoadSceneAsync("MainLevel");
            for(int i=0;i<6;i++)yield return null;
            Call(_panel,"TogglePanel");Assert.AreNotSame(oldManager,Field(_panel,"_abilityManager"));
            Assert.AreEqual(1,Call(_panel,"GrantItems",FindItem((IList)Field(_panel,"_items"),"clover_coin"),1));
            yield return null;
        }

        /// <summary>图形专项尝试捕获最终屏幕，包含 IMGUI；普通逻辑门禁不请求截图。</summary>
        private IEnumerator Capture()
        {
            string path=Environment.GetEnvironmentVariable("F7_SCREENSHOT");if(string.IsNullOrEmpty(path))yield break;
            Assert.AreNotEqual(UnityEngine.Rendering.GraphicsDeviceType.Null,SystemInfo.graphicsDeviceType);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            ScreenCapture.CaptureScreenshot(path);
            for(int i=0;i<120&&!File.Exists(path);i++)yield return null;
            Assert.IsTrue(File.Exists(path),"未取得包含 F7 窗口的图形截图");
        }

        /// <summary>按稳定 ID 从面板实际列表定位待测道具。</summary>
        private static object FindItem(IList items,string id)
        {foreach(object item in items)if((string)item.GetType().GetField("abilityID").GetValue(item)==id)return item;throw new InvalidOperationException(id);}
        /// <summary>解析默认运行时程序集的类型。</summary>
        private static Type TypeOf(string name)=>Type.GetType(name+", Assembly-CSharp",true);
        /// <summary>读取开发窗口内部状态。</summary>
        private static object Field(object obj,string name)=>obj.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(obj);
        /// <summary>调用实际开关及按钮共用的方法。</summary>
        private static object Call(object obj,string name,params object[] args)=>obj.GetType().GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(obj,args);
        /// <summary>替换测试账号存储，避免触碰用户真实账号数据。</summary>
        private static void ResetAccount()=>TypeOf("AccountProgressService").GetMethod("SetStorageForTests").Invoke(null,new[]{Activator.CreateInstance(TypeOf("InMemoryAccountProgressStorage"))});
    }
}
