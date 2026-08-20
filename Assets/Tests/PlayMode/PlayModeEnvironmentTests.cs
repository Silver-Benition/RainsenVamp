using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace RainsenVampSur.Tests.PlayMode
{
    /// <summary>验证 Test Runner 已经进入真实 PlayMode，并由 Player Loop 推进测试协程。</summary>
    public sealed class PlayModeEnvironmentTests
    {
        /// <summary>等待一帧后，帧计数应当递增且应用仍处于运行状态。</summary>
        [UnityTest]
        public IEnumerator PlayerLoop_进入PlayMode后_能够推进一帧()
        {
            Assert.IsTrue(Application.isPlaying);
            int initialFrame = Time.frameCount;

            yield return null;

            Assert.That(Time.frameCount, Is.GreaterThan(initialFrame));
        }
    }
}
