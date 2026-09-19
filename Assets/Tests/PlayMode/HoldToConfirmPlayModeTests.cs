using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace RainsenVampSur.Tests.PlayMode
{
    /// <summary>真实暂停时间下验证长按取消、单次提交及重新按下，不依赖奖励业务的内部实现。</summary>
    public sealed class HoldToConfirmPlayModeTests : PlayModeComponentTestBase
    {
        [UnityTest]
        public IEnumerator Hold_CancelReleaseExitDisableAndFocus_ThenOnlyOneCommitPerPress()
        {
            var events = new GameObject("HoldTestEvents", typeof(EventSystem)); TrackObject(events);
            var go = new GameObject("HoldTestButton", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)); TrackObject(go);
            Component hold = RuntimeComponentTestUtility.AddRuntimeComponent(go, "HoldToConfirmButton");
            Button button = (Button)hold; int commits = 0; button.onClick.AddListener(() => commits++);
            var pointer = new PointerEventData(events.GetComponent<EventSystem>()) { button = PointerEventData.InputButton.Left };
            Time.timeScale = 0;
            ExecuteEvents.Execute(go, pointer, ExecuteEvents.pointerDownHandler);
            yield return new WaitForSecondsRealtime(.25f);
            Assert.That(RuntimeComponentTestUtility.GetProperty<float>(hold, "Progress"), Is.InRange(.1f, .9f));
            ExecuteEvents.Execute(go, pointer, ExecuteEvents.pointerUpHandler);
            ExecuteEvents.Execute(go, pointer, ExecuteEvents.pointerClickHandler);
            Assert.AreEqual(0, commits);
            Assert.AreEqual(0, RuntimeComponentTestUtility.GetProperty<float>(hold, "Progress"));
            ExecuteEvents.Execute(go, pointer, ExecuteEvents.pointerDownHandler);
            ExecuteEvents.Execute(go, pointer, ExecuteEvents.pointerExitHandler);
            yield return new WaitForSecondsRealtime(.9f); Assert.AreEqual(0, commits);
            ExecuteEvents.Execute(go, pointer, ExecuteEvents.pointerDownHandler);
            ExecuteEvents.Execute(go, pointer, ExecuteEvents.deselectHandler);
            yield return new WaitForSecondsRealtime(.9f); Assert.AreEqual(0, commits);
            ExecuteEvents.Execute(go, pointer, ExecuteEvents.pointerDownHandler);
            go.SetActive(false); go.SetActive(true);
            yield return new WaitForSecondsRealtime(.9f); Assert.AreEqual(0, commits);
            ExecuteEvents.Execute(go, pointer, ExecuteEvents.pointerDownHandler);
            button.interactable = false;
            yield return new WaitForSecondsRealtime(.9f); Assert.AreEqual(0, commits);
            button.interactable = true;
            ExecuteEvents.Execute(go, pointer, ExecuteEvents.pointerDownHandler);
            RuntimeComponentTestUtility.Invoke(hold, "OnApplicationFocus", false);
            yield return new WaitForSecondsRealtime(.9f); Assert.AreEqual(0, commits);
            ExecuteEvents.Execute(go, pointer, ExecuteEvents.pointerDownHandler);
            yield return new WaitForSecondsRealtime(.95f); Assert.AreEqual(1, commits);
            yield return new WaitForSecondsRealtime(.95f); Assert.AreEqual(1, commits);
            ExecuteEvents.Execute(go, pointer, ExecuteEvents.pointerUpHandler);
            ExecuteEvents.Execute(go, pointer, ExecuteEvents.pointerClickHandler); Assert.AreEqual(1, commits);
            ExecuteEvents.Execute(go, pointer, ExecuteEvents.pointerDownHandler);
            yield return new WaitForSecondsRealtime(.95f); Assert.AreEqual(2, commits);
        }
    }
}
