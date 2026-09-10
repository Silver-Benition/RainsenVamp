#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using DiagnosticsProcess = System.Diagnostics.Process;
using DiagnosticsStartInfo = System.Diagnostics.ProcessStartInfo;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Session 21 性能测试资产生成和 Windows Development Build 入口。
/// 生成器只在 Assets/Tests/Performance/Generated 写入副本，再把副本绑定到 MainLevel
/// 场景拷贝；普通 MainLevel、原始世界线和 ProjectSettings 不会被改写。
/// </summary>
public static class MainWorldPerformanceBuild
{
    private const string GeneratedRoot = "Assets/Tests/Performance/Generated";
    private const string ScenePath = GeneratedRoot + "/MainWorldPerformance.unity";
    private const string ProfilePath = GeneratedRoot + "/MainWorldPerformanceProfile.asset";
    private const string WavePath = GeneratedRoot + "/MainWorldPerformanceGrassWaveConfig.asset";
    private const string WorldPath = GeneratedRoot + "/MainWorldPerformanceGrassWorldLine.asset";
    private const string CapacityCharacterPath = GeneratedRoot + "/MainWorldPerformanceCapacityCharacter.asset";
    private const string BossEncounterPath = GeneratedRoot + "/MainWorldPerformanceDelayedBossEncounter.asset";
    private const string WeakDataPath = GeneratedRoot + "/MainWorldPerformanceWeakEnemy.asset";
    private const string RangedDataPath = GeneratedRoot + "/MainWorldPerformanceRangedEnemy.asset";
    private const string DropTablePath = GeneratedRoot + "/MainWorldPerformanceWeakEnemyDropTable.asset";
    private const string RangedAttackPath = GeneratedRoot + "/MainWorldPerformanceRangedAttack.asset";
    private const string WeakPrefabPath = GeneratedRoot + "/MainWorldPerformanceWeakEnemy.prefab";
    private const string RangedPrefabPath = GeneratedRoot + "/MainWorldPerformanceRangedEnemy.prefab";
    private const string BuildPath = "Builds/Performance/MainWorldPerformance.exe";

    private const string GrassWaveSource = "Assets/Data/Map/GrassWaveConfig.asset";
    private const string GrassWorldSource = "Assets/Data/Map/GrassWorldLine.asset";
    private const string DefaultCharacterSource = "Assets/Data/Characters/DefaultCharacter.asset";
    private const string BossEncounterSource = "Assets/Data/Boss/ArmedColossusEncounter.asset";
    private const string WeakDataSource = "Assets/Data/WeakEnemy_1.asset";
    private const string RangedDataSource = "Assets/Data/RangedEnemy_1.asset";
    private const string DropTableSource = "Assets/Data/WeakEnemyDropTable.asset";
    private const string RangedAttackSource = "Assets/Data/RangedEnemyAttack_1.asset";
    private const string WeakPrefabSource = "Assets/Prefab/Enemy/EnemyWeak_1.prefab";
    private const string RangedPrefabSource = "Assets/Prefab/Enemy/EnemyRanged_1.prefab";
    private const string ExpPrefabPath = "Assets/Prefab/ExpGem.prefab";
    private const string CoinPrefabPath = "Assets/Prefab/Pickup/CoinPickup.prefab";

    /// <summary>在编辑器中生成或刷新性能测试专用场景、数据和 Prefab 副本。</summary>
    [MenuItem("RainsenVampSur/Performance/Generate Main World Test Assets")]
    public static void GenerateTestAssets()
    {
        EnsureGeneratedFolders();
        SceneSetup[] previousSceneSetup = EditorSceneManager.GetSceneManagerSetup();
        try
        {
            CopyAndConfigureDataAssets();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            GenerateProfileAsset();
            GenerateSceneAsset();
            // 场景副本在第一次生成 Profile 后才存在；第二次写入确保 sourceHash
            // 覆盖最终场景 YAML，而不是上一轮构建留下的旧内容。
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            GenerateProfileAsset();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[MainWorldPerformance] Generated test assets: " + GeneratedRoot);
        }
        finally
        {
            RestorePreviousScene(previousSceneSetup);
        }
    }

    /// <summary>生成副本后构建带 MAINWORLD_PERFORMANCE_BUILD 的 Windows Development Player。</summary>
    [MenuItem("RainsenVampSur/Performance/Build Windows Development Player")]
    public static void BuildWindowsDevelopment()
    {
        GenerateTestAssets();
        string absoluteBuildDirectory = Path.Combine(ProjectRoot, "Builds", "Performance");
        Directory.CreateDirectory(absoluteBuildDirectory);

        BuildPlayerOptions options = new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            locationPathName = Path.Combine(ProjectRoot, BuildPath),
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.Development | BuildOptions.AllowDebugging,
            extraScriptingDefines = new[] { "MAINWORLD_PERFORMANCE_BUILD" }
        };

        BuildReport report = BuildPipeline.BuildPlayer(options);
        if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
        {
            throw new InvalidOperationException("MainWorldPerformance Windows build failed: " + report.summary.result);
        }

        Debug.Log("[MainWorldPerformance] Windows Development Player built: " + options.locationPathName);
    }

    /// <summary>
    /// 批处理读取本次 CPU 原始记录，输出逐帧时间和慢帧内的耗时样本。
    /// 只在离线分析阶段使用，绝不在被测 Player 中遍历或格式化 Profiler 数据。
    /// </summary>
    public static void AnalyzeDiagnosticCapture()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Offline capture analysis requires batch mode.");
        string path = null;
        string[] arguments = Environment.GetCommandLineArgs();
        for (int index = 0; index + 1 < arguments.Length; index++)
            if (arguments[index] == "--perf-analyze") path = Path.GetFullPath(arguments[index + 1]);
        string allowedRoot = Path.GetFullPath(Path.Combine(ProjectRoot, "Logs", "Performance")) + Path.DirectorySeparatorChar;
        if (string.IsNullOrWhiteSpace(path) || !path.StartsWith(allowedRoot, StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
            throw new InvalidOperationException("Capture must exist under this QA project's Logs/Performance directory.");
        UnityEditorInternal.ProfilerDriver.enabled = false;
        // Unity 2022.3 默认只保留最后 300 帧，直接加载会漏掉切换前后的关键证据。
        // 这里使用该版本参考源码中确认的内部入口，仅改变即将退出的 batch 进程内存，
        // 不写 EditorPrefs。入口缺失时明确失败，不把末尾短片段冒充完整记录。
        System.Reflection.MethodInfo setHistory = typeof(UnityEditorInternal.ProfilerDriver).GetMethod(
            "SetMaxFrameHistoryLength", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        if (setHistory == null) throw new MissingMethodException("Unity 2022.3 SetMaxFrameHistoryLength unavailable.");
        setHistory.Invoke(null, new object[] { 65536 });
        if (!UnityEditorInternal.ProfilerDriver.LoadProfile(path, false))
            throw new InvalidOperationException("Profiler could not load capture: " + path);
        int first = UnityEditorInternal.ProfilerDriver.firstFrameIndex;
        int last = UnityEditorInternal.ProfilerDriver.lastFrameIndex;
        if (first < 0 || last < first) throw new InvalidOperationException("Capture contains no readable frames.");
        if (first > 1) throw new InvalidOperationException("Capture prefix was truncated by Profiler history: " + first);

        int validFrames = 0;
        using (StreamWriter frames = new StreamWriter(path + "-frames.csv"))
        using (StreamWriter samples = new StreamWriter(path + "-samples.csv"))
        {
            frames.WriteLine("profilerFrame,startMs,frameMs,sampleCount");
            samples.WriteLine("profilerFrame,thread,sampleIndex,startMs,durationMs,childrenRecursive,name");
            for (int frame = first; frame <= last; frame++)
            {
                using (UnityEditor.Profiling.RawFrameDataView data = UnityEditorInternal.ProfilerDriver.GetRawFrameDataView(frame, 0))
                {
                    if (!data.valid) continue;
                    validFrames++;
                    frames.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0},{1:R},{2:R},{3}",
                        frame, data.frameStartTimeMs, data.frameTimeMs, data.sampleCount));
                    // 慢帧保留完整耗时树的索引关系；正常帧仅保留诊断标记用于对齐时间轴。
                    bool slow = data.frameTimeMs > 33.33f;
                    WriteDiagnosticSamples(samples, data, frame, slow);
                    if (!slow) continue;
                    for (int thread = 1; thread < 128; thread++)
                    {
                        using (UnityEditor.Profiling.RawFrameDataView other = UnityEditorInternal.ProfilerDriver.GetRawFrameDataView(frame, thread))
                        {
                            if (!other.valid) break;
                            WriteDiagnosticSamples(samples, other, frame, true);
                        }
                    }
                }
            }
        }
        if (validFrames == 0) throw new InvalidOperationException("No valid main-thread frames were decoded.");
        File.WriteAllText(path + "-analysis.txt", string.Format(CultureInfo.InvariantCulture,
            "firstFrame={0}\nlastFrame={1}\nvalidFrames={2}\nSample durations are inclusive; do not sum parent and child times.\n", first, last, validFrames));
        Debug.Log("[MainWorldPerformance] CPU capture decoded: " + validFrames + " frames; " + path);
    }

    /// <summary>按原始样本序号导出包含关系；名称只在离线阶段解析，CSV 引号按标准转义。</summary>
    private static void WriteDiagnosticSamples(StreamWriter writer, UnityEditor.Profiling.RawFrameDataView data, int frame, bool slow)
    {
        int observation = data.GetMarkerId("PerformanceDiagnostic.FrameObservation");
        int flush = data.GetMarkerId("PerformanceDiagnostic.StageReportAndRawIO");
        int loadout = data.GetMarkerId("PerformanceDiagnostic.EnsureFullLoadout");
        for (int index = 0; index < data.sampleCount; index++)
        {
            int marker = data.GetSampleMarkerId(index);
            bool diagnostic = marker == observation || marker == flush || marker == loadout;
            float duration = data.GetSampleTimeMs(index);
            if (!diagnostic && (!slow || duration < 2f)) continue;
            string name = data.GetSampleName(index) ?? string.Empty;
            writer.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0},\"{1}\",{2},{3:R},{4:R},{5},\"{6}\"",
                frame, data.threadName.Replace("\"", "\"\""), index, data.GetSampleStartTimeMs(index), duration,
                data.GetSampleChildrenCountRecursive(index), name.Replace("\"", "\"\"")));
        }
    }

    private static string ProjectRoot => Directory.GetParent(Application.dataPath).FullName;

    /// <summary>创建生成目录，所有后续写入均限制在该目录或 Builds/Performance、Logs/Performance。</summary>
    private static void EnsureGeneratedFolders()
    {
        EnsureFolder("Assets/Tests");
        EnsureFolder("Assets/Tests/Performance");
        EnsureFolder(GeneratedRoot);
    }

    /// <summary>通过 AssetDatabase 创建一层目录，避免文件系统目录与 Unity 导入状态脱节。</summary>
    private static void EnsureFolder(string folderPath)
    {
        if (AssetDatabase.IsValidFolder(folderPath)) return;
        string parent = Path.GetDirectoryName(folderPath).Replace('\\', '/');
        string name = Path.GetFileName(folderPath);
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, name);
    }

    /// <summary>从当前 MainLevel 的正式资产复制生成配置，并重绑真实敌人生成链。</summary>
    private static void CopyAndConfigureDataAssets()
    {
        CopyAsset(GrassWaveSource, WavePath);
        CopyAsset(GrassWorldSource, WorldPath);
        CopyAsset(DefaultCharacterSource, CapacityCharacterPath);
        CopyAsset(BossEncounterSource, BossEncounterPath);
        CopyAsset(WeakDataSource, WeakDataPath);
        CopyAsset(RangedDataSource, RangedDataPath);
        CopyAsset(DropTableSource, DropTablePath);
        CopyAsset(RangedAttackSource, RangedAttackPath);
        CopyAsset(WeakPrefabSource, WeakPrefabPath);
        CopyAsset(RangedPrefabSource, RangedPrefabPath);

        WaveConfigSO wave = LoadRequired<WaveConfigSO>(WavePath);
        WorldLineDataSO world = LoadRequired<WorldLineDataSO>(WorldPath);
        CharacterDataSO capacityCharacter = LoadRequired<CharacterDataSO>(CapacityCharacterPath);
        BossEncounterDataSO bossEncounter = LoadRequired<BossEncounterDataSO>(BossEncounterPath);
        EnemyDataSO weakData = LoadRequired<EnemyDataSO>(WeakDataPath);
        EnemyDataSO rangedData = LoadRequired<EnemyDataSO>(RangedDataPath);
        EnemyDropTableSO dropTable = LoadRequired<EnemyDropTableSO>(DropTablePath);
        RangedEnemyAttackDataSO rangedAttack = LoadRequired<RangedEnemyAttackDataSO>(RangedAttackPath);
        GameObject weakPrefab = ConfigureEnemyPrefab(WeakPrefabPath, weakData, null);
        GameObject rangedPrefab = ConfigureEnemyPrefab(RangedPrefabPath, rangedData, rangedAttack);

        weakData.dropTable = dropTable;
        rangedData.dropTable = dropTable;
        weakData.dropExpPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ExpPrefabPath);
        rangedData.dropExpPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ExpPrefabPath);
        EditorUtility.SetDirty(weakData);
        EditorUtility.SetDirty(rangedData);

        if (wave.rules == null || wave.rules.Count < 2)
        {
            throw new InvalidOperationException("GrassWaveConfig must contain weak and ranged rules.");
        }

        wave.rules[0].enemyPrefab = weakPrefab;
        wave.rules[1].enemyPrefab = rangedPrefab;
        EditorUtility.SetDirty(wave);

        SetWorldReferences(world, wave, weakPrefab);
        capacityCharacter.baseStats = capacityCharacter.baseStats ?? new CharacterBaseStats();
        capacityCharacter.baseStats.maxHealth = 100000f;
        EditorUtility.SetDirty(capacityCharacter);
        bossEncounter.triggerTimeSeconds = 999999f;
        EditorUtility.SetDirty(bossEncounter);
    }

    /// <summary>复制一个 Unity 资产，先清理同名生成副本以保证每次构建可复现。</summary>
    private static void CopyAsset(string sourcePath, string destinationPath)
    {
        if (!File.Exists(Path.Combine(ProjectRoot, sourcePath)))
        {
            throw new FileNotFoundException("Performance source asset not found", sourcePath);
        }

        string absoluteDestination = Path.Combine(ProjectRoot, destinationPath);
        if (File.Exists(absoluteDestination))
        {
            // 直接覆盖资产内容并保留现有 .meta/GUID，减少场景和 Profile 在每次构建中的
            // 引用抖动；生成目录仍是唯一写入范围。
            File.Copy(Path.Combine(ProjectRoot, sourcePath), absoluteDestination, true);
            AssetDatabase.ImportAsset(destinationPath, ImportAssetOptions.ForceUpdate);
            return;
        }

        if (!AssetDatabase.CopyAsset(sourcePath, destinationPath))
        {
            throw new InvalidOperationException("Unable to copy performance asset: " + sourcePath);
        }
    }

    /// <summary>按生成路径读取资产并在缺失时立即终止，避免产生半有效性能配置。</summary>
    private static T LoadRequired<T>(string assetPath) where T : UnityEngine.Object
    {
        T asset = AssetDatabase.LoadAssetAtPath<T>(assetPath);
        if (asset == null) throw new InvalidOperationException("Generated asset missing or wrong type: " + assetPath);
        return asset;
    }

    /// <summary>把副本世界线绑定到副本波次，确保 MainWorld 仍使用真实 WorldWaveManager 链。</summary>
    private static void SetWorldReferences(WorldLineDataSO world, WaveConfigSO wave, GameObject weakPrefab)
    {
        SerializedObject serializedWorld = new SerializedObject(world);
        SerializedProperty waveProperty = serializedWorld.FindProperty("waveConfig");
        if (waveProperty == null) throw new InvalidOperationException("WorldLineDataSO.waveConfig property was not found.");
        waveProperty.objectReferenceValue = wave;
        serializedWorld.ApplyModifiedPropertiesWithoutUndo();

        SerializedProperty testEnemyProperty = serializedWorld.FindProperty("testEnemyPrefab");
        if (testEnemyProperty != null)
        {
            testEnemyProperty.objectReferenceValue = weakPrefab;
            serializedWorld.ApplyModifiedPropertiesWithoutUndo();
        }
        EditorUtility.SetDirty(world);
    }

    /// <summary>重绑复制敌人 Prefab 的正式 EnemyBase 数据和 RangedEnemyController 攻击数据。</summary>
    private static GameObject ConfigureEnemyPrefab(
        string prefabPath,
        EnemyDataSO enemyData,
        RangedEnemyAttackDataSO rangedAttack)
    {
        GameObject prefabContents = PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            EnemyBase enemyBase = prefabContents.GetComponent<EnemyBase>();
            if (enemyBase == null) throw new InvalidOperationException("Enemy prefab has no EnemyBase: " + prefabPath);
            enemyBase.enemyData = enemyData;

            if (rangedAttack != null)
            {
                RangedEnemyController rangedController = prefabContents.GetComponent<RangedEnemyController>();
                if (rangedController == null)
                {
                    throw new InvalidOperationException("Ranged prefab has no RangedEnemyController: " + prefabPath);
                }

                SerializedObject serializedRanged = new SerializedObject(rangedController);
                SerializedProperty attackProperty = serializedRanged.FindProperty("attackData");
                if (attackProperty == null)
                {
                    throw new InvalidOperationException("RangedEnemyController.attackData property was not found.");
                }

                attackProperty.objectReferenceValue = rangedAttack;
                serializedRanged.ApplyModifiedPropertiesWithoutUndo();
            }

            PrefabUtility.SaveAsPrefabAsset(prefabContents, prefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(prefabContents);
        }

        return AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
    }

    /// <summary>创建或更新 Profile，并写入源提交和生成内容哈希。</summary>
    private static void GenerateProfileAsset()
    {
        MainWorldPerformanceProfile profile = AssetDatabase.LoadAssetAtPath<MainWorldPerformanceProfile>(ProfilePath);
        if (profile == null)
        {
            profile = ScriptableObject.CreateInstance<MainWorldPerformanceProfile>();
            AssetDatabase.CreateAsset(profile, ProfilePath);
        }

        profile.profileId = "session21-main-world-performance";
        profile.defaultMode = MainWorldPerformanceRunMode.CapacitySweep;
        profile.defaultEventMode = MainWorldPerformanceEventMode.Controlled;
        profile.enableHarness = true;
        profile.autoStart = true;
        profile.fixedRandomSeed = 21021;
        profile.generatedMainWorld = LoadRequired<WorldLineDataSO>(WorldPath);
        profile.generatedWaveConfig = LoadRequired<WaveConfigSO>(WavePath);
        profile.generatedDropTable = LoadRequired<EnemyDropTableSO>(DropTablePath);
        profile.normalCharacter = LoadRequired<CharacterDataSO>(DefaultCharacterSource);
        profile.capacityCharacter = LoadRequired<CharacterDataSO>(CapacityCharacterPath);
        profile.delayedBossEncounter = LoadRequired<BossEncounterDataSO>(BossEncounterPath);
        profile.expPickupPrefab = LoadRequired<GameObject>(ExpPrefabPath);
        profile.coinPickupPrefab = LoadRequired<GameObject>(CoinPrefabPath);
        profile.fullLoadout = new[]
        {
            LoadRequired<WeaponDataSO>("Assets/Data/FireBall.asset"),
            LoadRequired<WeaponDataSO>("Assets/Data/Knife.asset"),
            LoadRequired<WeaponDataSO>("Assets/Data/Axe.asset"),
            LoadRequired<WeaponDataSO>("Assets/Data/Aura.asset"),
            LoadRequired<WeaponDataSO>("Assets/Data/Umbrella.asset")
        };
        profile.capacityMaximumHealthOverride = 100000f;
        profile.capacityExperienceRequirementOverride = 100000000f;
        // 仅提高生成测试副本的补充速率，保持敌人生命、武器和正式掉落表不变，
        // 以便近限完整武器阶段能维持目标人口并明确记录该测试覆盖条件。
        profile.capacityWaveSupplementalRateMultiplier = 4f;
        profile.sampling.expectedMaximumFramesPerSecond = 2048;
        profile.sourceRevision = ReadGitRevision();
        profile.sourceHash = ComputeSourceHash();
        profile.generatedByUnityVersion = Application.unityVersion;
        EditorUtility.SetDirty(profile);
    }

    /// <summary>复制 MainLevel 并在首次场景 Awake 前锁定副世界、绑定生成主世界和 Runner。</summary>
    private static void GenerateSceneAsset()
    {
        const string sourceScenePath = "Assets/Scenes/MainLevel.unity";
        CopyAsset(sourceScenePath, ScenePath);

        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        GameObject subWorldObject = GameObject.Find("SubWorldRuntime");
        if (subWorldObject == null)
        {
            throw new InvalidOperationException("Generated scene is missing SubWorldRuntime.");
        }

        // SubWorldRuntime 必须在序列化场景加载前就是 inactive；否则 MapStreamManager.Awake
        // 会预热副世界区块和 AI，运行时再 SetActive(false) 已经太晚。
        subWorldObject.SetActive(false);

        WorldLineDataSO generatedWorld = LoadRequired<WorldLineDataSO>(WorldPath);
        WaveConfigSO generatedWave = LoadRequired<WaveConfigSO>(WavePath);
        BindMainWorldSceneObjects(generatedWorld, generatedWave);

        GameObject gameManager = GameObject.Find("GameManager");
        if (gameManager == null) throw new InvalidOperationException("Generated scene is missing GameManager.");
        MainWorldPerformanceRunner runner = gameManager.GetComponent<MainWorldPerformanceRunner>();
        if (runner == null) runner = gameManager.AddComponent<MainWorldPerformanceRunner>();
        SerializedObject serializedRunner = new SerializedObject(runner);
        serializedRunner.FindProperty("profile").objectReferenceValue =
            LoadRequired<MainWorldPerformanceProfile>(ProfilePath);
        serializedRunner.FindProperty("runOnStart").boolValue = true;
        serializedRunner.ApplyModifiedPropertiesWithoutUndo();

        RunDirector runDirector = UnityEngine.Object.FindObjectOfType<RunDirector>();
        if (runDirector != null)
        {
            SerializedObject serializedDirector = new SerializedObject(runDirector);
            SerializedProperty encounterProperty = serializedDirector.FindProperty("bossEncounter");
            if (encounterProperty != null)
            {
                encounterProperty.objectReferenceValue = LoadRequired<BossEncounterDataSO>(BossEncounterPath);
                serializedDirector.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene))
        {
            throw new InvalidOperationException("Unable to save generated performance scene.");
        }
    }

    /// <summary>同时替换场景内主世界各组件的世界线引用，保留协调器的两套 slot 引用结构。</summary>
    private static void BindMainWorldSceneObjects(WorldLineDataSO generatedWorld, WaveConfigSO generatedWave)
    {
        GameObject coordinatorObject = GameObject.Find("WorldLineCoordinator");
        GameObject mainWorldObject = GameObject.Find("MainWorldRuntime");
        if (coordinatorObject == null || mainWorldObject == null)
        {
            throw new InvalidOperationException("Generated scene is missing coordinator or MainWorldRuntime.");
        }

        WorldLineCoordinator coordinator = coordinatorObject.GetComponent<WorldLineCoordinator>();
        MapStreamManager mapStream = mainWorldObject.GetComponent<MapStreamManager>();
        WorldEnemySimulation simulation = mainWorldObject.GetComponent<WorldEnemySimulation>();
        WorldWaveManager waveManager = mainWorldObject.GetComponent<WorldWaveManager>();
        if (coordinator == null || mapStream == null || simulation == null || waveManager == null)
        {
            throw new InvalidOperationException("Generated MainWorldRuntime references are incomplete.");
        }

        SerializedObject serializedCoordinator = new SerializedObject(coordinator);
        SerializedProperty mainSlot = serializedCoordinator.FindProperty("mainWorld");
        SerializedProperty mainWorldLine = mainSlot != null ? mainSlot.FindPropertyRelative("worldLine") : null;
        if (mainWorldLine == null) throw new InvalidOperationException("WorldLineCoordinator.mainWorld.worldLine was not found.");
        mainWorldLine.objectReferenceValue = generatedWorld;
        serializedCoordinator.ApplyModifiedPropertiesWithoutUndo();

        SerializedObject serializedMap = new SerializedObject(mapStream);
        SerializedProperty initialWorldLine = serializedMap.FindProperty("initialWorldLine");
        if (initialWorldLine != null)
        {
            initialWorldLine.objectReferenceValue = generatedWorld;
            serializedMap.ApplyModifiedPropertiesWithoutUndo();
        }

        SerializedObject serializedSimulation = new SerializedObject(simulation);
        SerializedProperty simulationWorldLine = serializedSimulation.FindProperty("worldLine");
        if (simulationWorldLine != null)
        {
            simulationWorldLine.objectReferenceValue = generatedWorld;
            serializedSimulation.ApplyModifiedPropertiesWithoutUndo();
        }

        SerializedObject serializedWaveManager = new SerializedObject(waveManager);
        SerializedProperty waveWorldLine = serializedWaveManager.FindProperty("worldLine");
        if (waveWorldLine != null)
        {
            waveWorldLine.objectReferenceValue = generatedWorld;
            serializedWaveManager.ApplyModifiedPropertiesWithoutUndo();
        }

        // worldLine 的 WaveConfig 是最终权威；同时校验 Profile 指向同一个副本，防止
        // 生成资产成功但场景仍悄悄运行原始 GrassWaveConfig。
        if (generatedWorld.WaveConfig != generatedWave)
        {
            throw new InvalidOperationException("Generated world and wave config references diverged.");
        }
    }

    /// <summary>生成器结束后恢复调用前的编辑器场景，避免改变用户当前工作上下文。</summary>
    private static void RestorePreviousScene(SceneSetup[] previousSceneSetup)
    {
        if (previousSceneSetup != null && previousSceneSetup.Length > 0)
        {
            EditorSceneManager.RestoreSceneManagerSetup(previousSceneSetup);
            return;
        }

        // 生成器可能由无场景的 batchmode 编辑器调用。Unity 不允许恢复为零场景，
        // 所以用空的 Single 场景结束，避免生成场景泄漏到后续 Editor/PlayMode 测试。
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
    }

    /// <summary>读取当前 QA checkout 的 HEAD，报告明确标识生成二进制对应的源提交。</summary>
    private static string ReadGitRevision()
    {
        try
        {
            DiagnosticsStartInfo startInfo = new DiagnosticsStartInfo
            {
                FileName = "git",
                Arguments = "-C \"" + ProjectRoot + "\" rev-parse HEAD",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            using (DiagnosticsProcess process = DiagnosticsProcess.Start(startInfo))
            {
                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit(5000);
                if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output)) return output.Trim();
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning("[MainWorldPerformance] 无法读取 git revision：" + exception.Message);
        }

        return "unknown";
    }

    /// <summary>对测试运行器、生成场景和副本配置计算内容哈希，避免只凭主仓 SHA 识别实验输入。</summary>
    private static string ComputeSourceHash()
    {
        string[] relativePaths =
        {
            "Assets/Scripts/Core/Diagnostics/MainWorldPerformanceProfile.cs",
            "Assets/Scripts/Core/Diagnostics/PerformanceSampler.cs",
            "Assets/Scripts/Core/Diagnostics/MainWorldPerformanceRunner.cs",
            "Assets/Editor/MainWorldPerformanceBuild.cs",
            "Assets/Scripts/Core/AccountProgressService.cs",
            "Assets/Scenes/MainLevel.unity",
            ScenePath,
            WavePath,
            WorldPath,
            CapacityCharacterPath,
            BossEncounterPath,
            WeakDataPath,
            RangedDataPath,
            DropTablePath,
            RangedAttackPath,
            WeakPrefabPath,
            RangedPrefabPath
        };

        Array.Sort(relativePaths, StringComparer.Ordinal);
        using (SHA256 sha = SHA256.Create())
        using (MemoryStream stream = new MemoryStream())
        {
            for (int index = 0; index < relativePaths.Length; index++)
            {
                string relativePath = relativePaths[index];
                string absolutePath = Path.Combine(ProjectRoot, relativePath);
                if (!File.Exists(absolutePath)) continue;
                byte[] pathBytes = Encoding.UTF8.GetBytes(relativePath + "\n");
                byte[] contentBytes = File.ReadAllBytes(absolutePath);
                stream.Write(pathBytes, 0, pathBytes.Length);
                stream.Write(contentBytes, 0, contentBytes.Length);
            }

            byte[] digest = sha.ComputeHash(stream.ToArray());
            StringBuilder builder = new StringBuilder(digest.Length * 2);
            for (int index = 0; index < digest.Length; index++)
            {
                builder.Append(digest[index].ToString("x2", CultureInfo.InvariantCulture));
            }

            return builder.ToString();
        }
    }
}
#endif
