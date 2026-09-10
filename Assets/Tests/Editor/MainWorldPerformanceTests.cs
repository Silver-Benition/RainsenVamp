using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RainsenVampSur.Tests
{
    /// <summary>验证 Session 21 生成资产隔离、报告语义和普通场景未自动注入 Runner。</summary>
    public sealed class MainWorldPerformanceTests
    {
        /// <summary>连续记录必须保留工具边界帧，并在溢出时明确报出证据缺口。</summary>
        [Test]
        public void DiagnosticTrace_PreservesBoundaryFramesAndReportsOverflow()
        {
            MainWorldPerformanceDiagnosticTrace trace = new MainWorldPerformanceDiagnosticTrace(2);
            trace.Record(101, 1.25d, 437f, 3, 499, true);
            trace.Record(102, 1.26d, 10f, 4, 500, false);
            trace.Record(103, 1.27d, 10f, 4, 500, false);
            using (System.IO.StringWriter writer = new System.IO.StringWriter())
            {
                trace.WriteCsv(writer);
                StringAssert.Contains("101,1.25,437,3,499,1,", writer.ToString());
                StringAssert.Contains("102,1.26,10,4,500,0,", writer.ToString());
                StringAssert.DoesNotContain("103,", writer.ToString());
            }
            Assert.AreEqual(2, trace.Count);
            Assert.AreEqual(1, trace.Dropped);
        }

        /// <summary>长帧诊断额外记录百毫秒比例，同时保留原有分位数和五十毫秒口径。</summary>
        [Test]
        public void Sampler_ReportsLongFrameThresholdsWithoutDroppingOutliers()
        {
            using (PerformanceSampler sampler = new PerformanceSampler(1024))
            {
                sampler.BeginStage(MainWorldPerformanceStageKind.Steady, 500, 0.9f, false);
                sampler.RecordFrame(0.01f, 500, 0, 0, 0, true);
                sampler.RecordFrame(0.06f, 500, 0, 0, 0, true);
                sampler.RecordFrame(0.437f, 500, 0, 0, 0, true);
                MainWorldPerformanceFrameStatistics result = sampler.EndStage(out int minimum, out int maximum);
                Assert.That(result.ratioOver100Milliseconds, Is.EqualTo(1f / 3f).Within(0.0001f));
                Assert.That(result.ratioOver50Milliseconds, Is.EqualTo(2f / 3f).Within(0.0001f));
                Assert.That(result.maximumMilliseconds, Is.EqualTo(437f).Within(0.01f));
            }
        }

        private const string GeneratedRoot = "Assets/Tests/Performance/Generated/";
        private const string GeneratedScenePath = GeneratedRoot + "MainWorldPerformance.unity";
        private const string ProfilePath = GeneratedRoot + "MainWorldPerformanceProfile.asset";
        private const string MainScenePath = "Assets/Scenes/MainLevel.unity";

        /// <summary>生成 Profile 应指向同一组主世界和波次副本，并保留正常角色作为对照。</summary>
        [Test]
        public void GeneratedProfile_ReferencesIsolatedMainWorldAndNormalCharacter()
        {
            MainWorldPerformanceProfile profile = AssetDatabase.LoadAssetAtPath<MainWorldPerformanceProfile>(ProfilePath);
            Assert.IsNotNull(profile, "GenerateTestAssets must run before performance tests.");
            Assert.IsTrue(profile.IsValid);
            Assert.IsNotNull(profile.normalCharacter);
            Assert.IsNotNull(profile.capacityCharacter);
            Assert.IsNotNull(profile.generatedDropTable);
            Assert.AreEqual(MainWorldPerformanceEventMode.Controlled, profile.defaultEventMode);
            Assert.AreSame(profile.generatedWaveConfig, profile.generatedMainWorld.WaveConfig);
            Assert.That(profile.sourceRevision, Does.Match("^[0-9a-f]{40}$"));
            Assert.AreNotEqual("unknown", profile.sourceRevision.ToLowerInvariant());
            Assert.That(profile.capacityMaximumHealthOverride, Is.GreaterThan(100f));
            Assert.That(profile.capacityExperienceRequirementOverride, Is.GreaterThan(1000000f));
            Assert.That(profile.capacityTiers.Length, Is.GreaterThanOrEqualTo(3));
        }

        /// <summary>生成场景应在序列化加载前停用副世界，同时保留协调器两套有效引用。</summary>
        [Test]
        public void GeneratedScene_DisablesSubWorldBeforePlayAndKeepsCoordinatorReferences()
        {
            SceneSetup[] previousSetup = EditorSceneManager.GetSceneManagerSetup();
            try
            {
                Scene scene = EditorSceneManager.OpenScene(GeneratedScenePath, OpenSceneMode.Single);
                GameObject subWorld = FindInScene(scene, "SubWorldRuntime");
                GameObject mainWorld = FindInScene(scene, "MainWorldRuntime");
                GameObject gameManager = FindInScene(scene, "GameManager");
                WorldLineCoordinator coordinator = FindComponentInScene<WorldLineCoordinator>(scene);
                MainWorldPerformanceRunner runner = gameManager != null
                    ? gameManager.GetComponent<MainWorldPerformanceRunner>()
                    : null;

                Assert.IsNotNull(subWorld);
                Assert.IsFalse(subWorld.activeSelf);
                Assert.IsNotNull(mainWorld);
                Assert.IsNotNull(coordinator);
                Assert.IsNotNull(coordinator.MainWorldWaveManager);
                Assert.IsNotNull(coordinator.SubWorldWaveManager);
                Assert.IsNotNull(runner);
                Assert.IsNotNull(runner.Profile);
                Assert.AreEqual("main", runner.Profile.generatedMainWorld.WorldLineId);
            }
            finally
            {
                RestoreSceneManagerSetup(previousSetup);
            }
        }

        /// <summary>普通 MainLevel 不应含 Runner 或性能专用编译符号，避免自动重置账号和波次。</summary>
        [Test]
        public void OrdinaryMainLevel_DoesNotAutoActivatePerformanceHarness()
        {
            SceneSetup[] previousSetup = EditorSceneManager.GetSceneManagerSetup();
            try
            {
                Scene scene = EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);
                Assert.IsNull(FindComponentInScene<MainWorldPerformanceRunner>(scene));
                string defines = PlayerSettings.GetScriptingDefineSymbolsForGroup(BuildTargetGroup.Standalone);
                StringAssert.DoesNotContain("MAINWORLD_PERFORMANCE_BUILD", defines);
            }
            finally
            {
                RestoreSceneManagerSetup(previousSetup);
            }
        }

        /// <summary>报告必须明确区分可用计数、缺失计数、负载不足、溢出和中断。</summary>
        [Test]
        public void ReportEvaluator_RejectsMissingLoadOverflowAndInterrupt()
        {
            MainWorldPerformanceStageReport valid = new MainWorldPerformanceStageReport
            {
                frameSampleCount = 600,
                minimumObservedActiveEnemies = 90,
                requestedTargetActiveEnemies = 100,
                frame = new MainWorldPerformanceFrameStatistics
                {
                    support = new MainWorldPerformanceCounterSupport { mainThread = true, renderThread = true, gpu = true }
                }
            };
            Assert.IsTrue(MainWorldPerformanceReportEvaluator.MeetsTargetCoverage(90, 100, 0.9f));
            Assert.IsTrue(MainWorldPerformanceReportEvaluator.IsStableCapacityEvidence(valid));

            valid.insufficientLoad = true;
            Assert.IsFalse(MainWorldPerformanceReportEvaluator.IsStableCapacityEvidence(valid));
            valid.insufficientLoad = false;
            valid.capacityOverflowed = true;
            Assert.IsFalse(MainWorldPerformanceReportEvaluator.IsStableCapacityEvidence(valid));
            valid.capacityOverflowed = false;
            valid.interrupted = true;
            Assert.IsFalse(MainWorldPerformanceReportEvaluator.IsStableCapacityEvidence(valid));

            MainWorldPerformanceStageReport insufficient = new MainWorldPerformanceStageReport
            {
                frameSampleCount = 1,
                insufficientSamples = true
            };
            Assert.IsFalse(MainWorldPerformanceReportEvaluator.IsStableCapacityEvidence(insufficient));
            Assert.That(MainWorldPerformanceCounterSupport.UnavailableValue, Is.EqualTo(-1f));
        }

        /// <summary>AB 对照必须拒绝污染、武器快照变化和不可比窗口，避免把差值伪报成开销。</summary>
        [Test]
        public void ReportEvaluator_RejectsUnmatchedInstrumentationWindows()
        {
            MainWorldPerformanceStageReport first = new MainWorldPerformanceStageReport
            {
                observedDurationSeconds = 5f,
                requestedTargetActiveEnemies = 500,
                targetCoverageRatio = 1f,
                frameSampleCount = 300,
                loadoutDescription = "ownedCount=5;known=full"
            };
            MainWorldPerformanceStageReport second = new MainWorldPerformanceStageReport
            {
                observedDurationSeconds = 5.01f,
                requestedTargetActiveEnemies = 500,
                targetCoverageRatio = 1f,
                frameSampleCount = 300,
                loadoutDescription = "ownedCount=5;known=full"
            };

            Assert.IsTrue(MainWorldPerformanceReportEvaluator.IsComparableInstrumentationWindow(
                first, second, 0.95f, 0.5f));
            second.contaminated = true;
            Assert.IsFalse(MainWorldPerformanceReportEvaluator.IsComparableInstrumentationWindow(
                first, second, 0.95f, 0.5f));
            second.contaminated = false;
            second.loadoutDescription = "ownedCount=4;known=starter";
            Assert.IsFalse(MainWorldPerformanceReportEvaluator.IsComparableInstrumentationWindow(
                first, second, 0.95f, 0.5f));
        }

        /// <summary>用固定帧时长验证均值、分位数、比例、原始时间和容量溢出语义。</summary>
        [Test]
        public void PerformanceSampler_UsesKnownValuesAndRejectsOverflow()
        {
            PerformanceSampler sampler = new PerformanceSampler(1024);
            try
            {
                sampler.BeginStage(MainWorldPerformanceStageKind.Steady, 0, 0.9f, false);
                sampler.RecordFrame(0.01f, 3, 4, 5, 2, true);
                sampler.RecordFrame(0.02f, 4, 5, 6, 3, true);
                sampler.RecordFrame(0.03f, 5, 6, 7, 4, true);
                MainWorldPerformanceFrameStatistics stats = sampler.EndStage(out int minimum, out int maximum);

                Assert.That(stats.averageMilliseconds, Is.EqualTo(20f).Within(0.001f));
                Assert.That(stats.p95Milliseconds, Is.EqualTo(30f).Within(0.001f));
                Assert.That(stats.maximumMilliseconds, Is.EqualTo(30f).Within(0.001f));
                Assert.That(stats.averageActiveProjectiles, Is.EqualTo(5));
                Assert.That(minimum, Is.EqualTo(3));
                Assert.That(maximum, Is.EqualTo(5));
                Assert.That(stats.averageMainThreadMilliseconds, Is.EqualTo(-1f));
                StringAssert.Contains("\"elapsedMs\":", sampler.CreateRawSamplesJson());
                StringAssert.Contains("\"frameMs\":10", sampler.CreateRawSamplesJson());

                sampler.BeginStage(MainWorldPerformanceStageKind.Steady, 0, 0.9f, false);
                for (int index = 0; index < 1100; index++)
                {
                    sampler.RecordFrame(0.001f, 0, 0, 0, 0, false);
                }

                Assert.IsTrue(sampler.CapacityOverflowed);
                Assert.That(sampler.DroppedFrameCount, Is.GreaterThan(0));
            }
            finally
            {
                sampler.Dispose();
            }
        }

        /// <summary>异常 GPU 样本必须保留原始值、使阶段计数器不可用并在下一阶段重新探测。</summary>
        [Test]
        public void PerformanceSampler_GpuOutlierIsUnavailableAndRawEvidenceIsPreserved()
        {
            PerformanceSampler sampler = new PerformanceSampler(16);
            try
            {
                sampler.BeginStage(MainWorldPerformanceStageKind.Steady, 0, 0.9f, true);
                sampler.InjectGpuSampleForTests(4f);
                sampler.InjectGpuSampleForTests(-1f); // Unity recorder 的 unavailable sentinel。
                sampler.InjectGpuSampleForTests(-2f); // 其他负值属于异常计数器结果。
                sampler.InjectGpuSampleForTests(1788829500000f);

                MainWorldPerformanceFrameStatistics invalidStats =
                    sampler.EndStage(out _, out _);
                string invalidRaw = sampler.CreateRawSamplesJson();

                Assert.That(sampler.GpuCounterAnomalyCount, Is.EqualTo(2));
                Assert.That(sampler.GpuCounterInvalidReason, Is.EqualTo("gpu-counter-outlier-over-60000ms"));
                Assert.That(invalidStats.averageGpuMilliseconds, Is.EqualTo(-1f));
                Assert.IsFalse(invalidStats.support.gpu);
                StringAssert.Contains("-2", invalidRaw);
                StringAssert.Contains("1.7888295E+12", invalidRaw);

                sampler.BeginStage(MainWorldPerformanceStageKind.Steady, 0, 0.9f, true);
                sampler.InjectGpuSampleForTests(8f);
                MainWorldPerformanceFrameStatistics nextStats = sampler.EndStage(out _, out _);
                Assert.That(sampler.GpuCounterAnomalyCount, Is.EqualTo(0));
                Assert.IsFalse(sampler.GpuCounterInvalidated);
                if (nextStats.support.gpu)
                {
                    Assert.That(nextStats.averageGpuMilliseconds, Is.EqualTo(8f).Within(0.001f));
                }
                else
                {
                    Assert.That(nextStats.averageGpuMilliseconds, Is.EqualTo(-1f));
                }
                StringAssert.Contains("\"gpuMs\":8", sampler.CreateRawSamplesJson());
            }
            finally
            {
                sampler.Dispose();
            }
        }

        /// <summary>采样器关闭阶段应释放额外计数器并保留最小帧时/敌人数采样，随后可重新开启。</summary>
        [Test]
        public void PerformanceSampler_ControlWindowDisablesCountersAndReopens()
        {
            PerformanceSampler sampler = new PerformanceSampler(64);
            try
            {
                sampler.BeginStage(MainWorldPerformanceStageKind.InstrumentationControl, 100, 0.9f, false);
                Assert.IsFalse(sampler.InstrumentationEnabled);
                sampler.RecordFrame(0.01f, 95, 12, 34, 56, true);
                MainWorldPerformanceFrameStatistics control = sampler.EndStage(out int minimum, out int maximum);

                Assert.That(control.averageMilliseconds, Is.EqualTo(10f).Within(0.001f));
                Assert.That(minimum, Is.EqualTo(95));
                Assert.That(maximum, Is.EqualTo(95));
                Assert.That(control.averageMainThreadMilliseconds, Is.EqualTo(-1f));
                Assert.That(control.averageGpuMilliseconds, Is.EqualTo(-1f));
                Assert.IsFalse(control.support.mainThread);
                Assert.IsFalse(control.support.gpu);

                sampler.BeginStage(MainWorldPerformanceStageKind.InstrumentationEnabled, 100, 0.9f, true);
                Assert.IsTrue(sampler.InstrumentationEnabled);
                sampler.RecordFrame(0.01f, 95, 12, 34, 56, true);
                MainWorldPerformanceFrameStatistics enabled = sampler.EndStage(out _, out _);
                Assert.That(enabled.averageMilliseconds, Is.EqualTo(10f).Within(0.001f));
            }
            finally
            {
                sampler.Dispose();
            }
        }

        /// <summary>生成目录不应覆盖正式 MainLevel 的 YAML 内容。</summary>
        [Test]
        public void GeneratedScene_IsSeparateAssetFromMainLevel()
        {
            string mainGuid = AssetDatabase.AssetPathToGUID(MainScenePath);
            string generatedGuid = AssetDatabase.AssetPathToGUID(GeneratedScenePath);
            Assert.IsNotEmpty(mainGuid);
            Assert.IsNotEmpty(generatedGuid);
            Assert.AreNotEqual(mainGuid, generatedGuid);
        }

        /// <summary>在当前场景中按名称读取对象，即使层级节点被停用也能用于隔离断言。</summary>
        private static GameObject FindInScene(Scene scene, string objectName)
        {
            GameObject[] roots = scene.GetRootGameObjects();
            for (int rootIndex = 0; rootIndex < roots.Length; rootIndex++)
            {
                Transform[] transforms = roots[rootIndex].GetComponentsInChildren<Transform>(true);
                for (int index = 0; index < transforms.Length; index++)
                {
                    if (transforms[index].name == objectName) return transforms[index].gameObject;
                }
            }

            return null;
        }

        /// <summary>在包含停用节点的场景中寻找首个指定组件。</summary>
        private static T FindComponentInScene<T>(Scene scene) where T : Component
        {
            GameObject[] roots = scene.GetRootGameObjects();
            for (int index = 0; index < roots.Length; index++)
            {
                T component = roots[index].GetComponentInChildren<T>(true);
                if (component != null) return component;
            }

            return null;
        }

        /// <summary>恢复原场景堆栈；无初始场景时建立空场景，避免 Unity 拒绝零场景状态。</summary>
        private static void RestoreSceneManagerSetup(SceneSetup[] previousSetup)
        {
            if (previousSetup != null && previousSetup.Length > 0)
            {
                EditorSceneManager.RestoreSceneManagerSetup(previousSetup);
                return;
            }

            // Unity 不能把 SceneManager 置于“没有活动场景”的状态；测试运行器若从
            // 空编辑器上下文开始，使用一个空 Single 场景作为确定性的清理终点。
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }
    }
}
