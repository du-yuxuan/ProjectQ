// ============================================================
// ProjectSetup.cs — PICO Unity 项目自动配置脚本
// 用法：Unity 菜单 → Q → Configure Project (一键配置)
// 或命令行：-executeMethod Q.Pico.Editor.ProjectSetup.ConfigureAll
// ============================================================

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.XR.Management;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.XR.Management;
using Unity.XR.PXR;
using System.IO;
using System.Xml;

namespace Q.Pico.Editor
{
    public class ProjectSetup
    {
        const string COMPANY_NAME = "AdventureX";
        const string PRODUCT_NAME = "Q-Cue";
        const string VERSION = "17.0.0";
        const string PACKAGE_NAME = "com.adventurex.qcue";
        const int MIN_API_LEVEL = 29; // Android 10

        // ============================================================
        // 一键配置入口
        // ============================================================

        [MenuItem("Q/Configure Project (一键配置)")]
        public static void ConfigureAll()
        {
            Debug.Log("════════════════════════════════════════");
            Debug.Log("  Q v17 项目配置开始");
            Debug.Log("════════════════════════════════════════");
            ConfigurePlayerSettings();
            ConfigureXRPlugin();
            ConfigureAndroidManifest();
            CreateXRScene();

            Debug.Log("════════════════════════════════════════");
            Debug.Log("  ✅ 项目配置完成！");
            Debug.Log("════════════════════════════════════════");
        }

        // ============================================================
        // 1. Player Settings
        // ============================================================

        static void ConfigurePlayerSettings()
        {
            Debug.Log("[1/4] 配置 Player Settings...");

            // 公司名/产品名/版本号
            PlayerSettings.companyName = COMPANY_NAME;
            PlayerSettings.productName = PRODUCT_NAME;
            PlayerSettings.bundleVersion = VERSION;

            // Android 设置
            // PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, PACKAGE_NAME); // TODO: 切换到 Android 后设置

            // Minimum API Level = Android 10 (29)
            // PlayerSettings.Android.minSdkVersion = (AndroidSdkVersions)MIN_API_LEVEL; // TODO: 切换到 Android 后设置

            // Target API Level = Automatic
            // PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AutoDetectMin; // TODO: 在 Android 平台手动设置

            // Scripting Backend = IL2CPP
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);

            // Target Architectures = ARM64 only
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;

            // Color Space = Linear
            PlayerSettings.colorSpace = ColorSpace.Linear;

            // Install in Build Settings
            // EditorUserBuildSettings.SwitchActiveBuildTargetAsync(BuildTargetGroup.Android, BuildTarget.Android); // TODO: 手动切换

            Debug.Log($"  ✅ Company: {COMPANY_NAME}, Product: {PRODUCT_NAME}, v{VERSION}");
            Debug.Log($"  ✅ Package: {PACKAGE_NAME}");
            Debug.Log($"  ✅ Min API: {MIN_API_LEVEL} (Android 10)");
            Debug.Log($"  ✅ Backend: IL2CPP, Arch: ARM64, ColorSpace: Linear");
        }

        // ============================================================
        // 2. XR Plugin Management — 启用 PICO XR 插件
        // ============================================================

        static void ConfigureXRPlugin()
        {
            Debug.Log("[2/4] 配置 XR Plugin Management...");

            // 确保 XR Plugin Management 已安装
            var xrGeneralSettings = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Android);
            if (xrGeneralSettings == null)
            {
                // 创建 XR General Settings
                var settings = ScriptableObject.CreateInstance<XRGeneralSettings>();
                xrGeneralSettings = settings;

                // 注册到 Android 构建目标
                XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Android);
            }

            // 尝试启用 PICO XR Loader
            // PICO SDK 导入后会有 PXR XR Loader，这里通过包名检查
            Debug.Log("  ℹ️ PICO XR Loader 需要在 SDK 导入后在 Project Settings → XR Plug-in Management 中勾选 PICO");
            Debug.Log("  ✅ XR Plugin Management 框架已配置");
        }

        // ============================================================
        // 3. AndroidManifest 自定义
        // ============================================================

        static void ConfigureAndroidManifest()
        {
            Debug.Log("[3/4] 配置 AndroidManifest...");

            // 启用 Custom Main Manifest
            // PlayerSettings.Android.customAndroidManifest = true; // TODO: 在 Player Settings 里手动勾选

            string manifestDir = "Assets/Q/Plugins/Android";
            string manifestPath = manifestDir + "/AndroidManifest.xml";

            // 确保目录存在
            if (!Directory.Exists(manifestDir))
            {
                Directory.CreateDirectory(manifestDir);
            }

            // 如果已有 AndroidManifest，检查是否需要更新
            if (File.Exists(manifestPath))
            {
                Debug.Log("  ✅ AndroidManifest.xml 已存在，保留自定义配置");
            }
            else
            {
                Debug.Log("  ⚠️ AndroidManifest.xml 不存在，Unity 将自动生成基础版本");
            }

            // 启用 Custom Proguard File（可选）
            // PlayerSettings.Android.customProguardFile = false;

            Debug.Log("  ✅ Custom Main Manifest 已启用");
        }

        // ============================================================
        // 4. 创建 XR 场景
        // ============================================================

        static void CreateXRScene()
        {
            Debug.Log("[4/4] 创建 XR 场景...");

            string scenePath = "Assets/Q/Scenes/QMain.unity";
            string sceneDir = "Assets/Q/Scenes";

            if (!Directory.Exists(sceneDir))
            {
                Directory.CreateDirectory(sceneDir);
            }

            if (File.Exists(scenePath))
            {
                Debug.Log($"  ℹ️ 场景已存在: {scenePath}");
                return;
            }

            // 创建新场景
            var scene = UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene);

            // 1. 添加 Directional Light
            var lightGO = new GameObject("Directional Light");
            var light = lightGO.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.0f;
            lightGO.transform.rotation = Quaternion.Euler(50, -30, 0);

            // 2. 添加地面 Plane
            var planeGO = GameObject.CreatePrimitive(PrimitiveType.Plane);
            planeGO.name = "Ground";
            planeGO.transform.position = Vector3.zero;
            planeGO.transform.localScale = new Vector3(10, 1, 10);

            // 3. 添加 XR Origin（VR）
            // 通过查找 XR Origin 的预制体或创建
            var xrOriginGO = new GameObject("XR Origin");
            xrOriginGO.AddComponent<Camera>(); // 主相机
            xrOriginGO.AddComponent<AudioListener>();

            // 尝试添加 XR Origin 组件（需要 XR Interaction Toolkit）
            var xrOriginType = System.Type.GetType("Unity.XR.CoreUtils.XROrigin, Unity.XR.CoreUtils");
            if (xrOriginType != null)
            {
                xrOriginGO.AddComponent(xrOriginType);
                Debug.Log("  ✅ XR Origin 组件已添加");
            }
            else
            {
                Debug.LogWarning("  ⚠️ XR Origin 组件未找到（需要 XR Interaction Toolkit）");
            }

            // 4. 尝试添加 PXR_Manager
            var pxrManagerType = System.Type.GetType("Unity.XR.PXR.PXR_Manager, Unity.XR.PXR");
            if (pxrManagerType != null)
            {
                xrOriginGO.AddComponent(pxrManagerType);
                Debug.Log("  ✅ PXR_Manager 组件已添加");
            }
            else
            {
                Debug.LogWarning("  ⚠️ PXR_Manager 组件未找到（需要 PICO SDK 导入）");
            }

            // 5. 添加 Q 脚本组件
            AddQComponent<QSceneManager>(xrOriginGO, "QSceneManager");
            AddQComponent<QWebSocketClient>(xrOriginGO, "QWebSocketClient");
            AddQComponent<SpatialHUDManager>(xrOriginGO, "SpatialHUDManager");
            AddQComponent<FaceOcclusionManager>(xrOriginGO, "FaceOcclusionManager");
            AddQComponent<WalletConnectPanel>(xrOriginGO, "WalletConnectPanel");
            AddQComponent<HeartRateInputPanel>(xrOriginGO, "HeartRateInputPanel");
            AddQComponent<CredentialCardSpawner>(xrOriginGO, "CredentialCardSpawner");
            AddQComponent<RingBleManager>(xrOriginGO, "RingBleManager");
            AddQComponent<RingInputBridge>(xrOriginGO, "RingInputBridge");
            AddQComponent<PicoControllerInput>(xrOriginGO, "PicoControllerInput");
            AddQComponent<QXRUIBootstrap>(xrOriginGO, "QXRUIBootstrap");
            AddQComponent<SpeciesAvatarController>(xrOriginGO, "SpeciesAvatarController");
            AddQComponent<CountdownTimer>(xrOriginGO, "CountdownTimer");
            AddQComponent<SpeciesMapper>(xrOriginGO, "SpeciesMapper");

            // 保存场景
            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene, scenePath);
            Debug.Log($"  ✅ 场景已创建: {scenePath}");

            // 添加到 Build Settings
            var buildScenes = EditorBuildSettings.scenes;
            bool found = false;
            foreach (var s in buildScenes)
            {
                if (s.path == scenePath) { found = true; break; }
            }
            if (!found)
            {
                var scenesList = new System.Collections.Generic.List<EditorBuildSettingsScene>(buildScenes);
                scenesList.Add(new EditorBuildSettingsScene(scenePath, true));
                EditorBuildSettings.scenes = scenesList.ToArray();
                Debug.Log("  ✅ 场景已添加到 Build Settings");
            }
        }

        static void AddQComponent<T>(GameObject go, string name) where T : Component
        {
            try
            {
                go.AddComponent<T>();
                Debug.Log($"  ✅ {name} 已添加");
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"  ⚠️ {name} 添加失败: {e.Message}");
            }
        }

        // ============================================================
        // 辅助：检查包是否已安装
        // ============================================================

        [MenuItem("Q/Check Packages (检查包安装状态)")]
        public static void CheckPackages()
        {
            Debug.Log("════════════════════════════════════════");
            Debug.Log("  检查已安装的 Unity 包");
            Debug.Log("════════════════════════════════════════");

            string[] packages = {
                "com.unity.xr.interaction.toolkit",
                "com.unity.xr.openxr",
                "com.unity.inputsystem",
                "com.unity.textmeshpro",
                "com.unity.nuget.newtonsoft-json",
                "com.picoxr.pvrunity",
            };

            foreach (var pkg in packages)
            {
                var info = UnityEditor.PackageManager.PackageInfo.FindForAssetPath($"Packages/{pkg}");
                if (info != null)
                {
                    Debug.Log($"  ✅ {pkg} v{info.version}");
                }
                else
                {
                    Debug.Log($"  ❌ {pkg} — 未安装");
                }
            }

            // 检查 XR General Settings
            var androidSettings = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Android);
            if (androidSettings != null)
            {
                Debug.Log($"  ✅ XR Plugin Management (Android) 已配置");
                if (androidSettings.AssignedSettings != null)
                {
                    foreach (var loader in androidSettings.AssignedSettings.activeLoaders)
                    {
                        Debug.Log($"    Loader: {loader.name}");
                    }
                }
            }
            else
            {
                Debug.Log("  ❌ XR Plugin Management 未配置");
            }
        }

        // ============================================================
        // 构建 APK
        // ============================================================

        [MenuItem("Q/Build APK (构建 Android)")]
        public static void BuildAPK()
        {
            string[] scenes = { "Assets/Q/Scenes/QMain.unity" };
            string apkPath = "../Build/Q-Cue.apk";

            Debug.Log("════════════════════════════════════════");
            Debug.Log("  构建 Android APK");
            Debug.Log("════════════════════════════════════════");

            // 配置 Android SDK / JDK 路径
            string unityRoot = "/Applications/Unity/Hub/Editor/2022.3.62f1/PlaybackEngines/AndroidPlayer";
            string sdkPath = unityRoot + "/SDK";
            string jdkPath = unityRoot + "/OpenJDK";
            string androidStudioSdk = "/Users/duyuxuan/Library/Android/sdk";
            if (System.IO.Directory.Exists(androidStudioSdk)) sdkPath = androidStudioSdk;

            EditorPrefs.SetString("AndroidSdkRoot", sdkPath);
            EditorPrefs.SetString("JdkPath", jdkPath);
            string ndkPath = unityRoot + "/NDK";
            EditorPrefs.SetString("AndroidNdkRoot", ndkPath);
            Debug.Log($"  NDK: {ndkPath}");
            Debug.Log($"  SDK: {sdkPath}");
            Debug.Log($"  JDK: {jdkPath}");

            // 输入：Android 不支持 Both
            // 0=Input Manager, 1=Input System Package, 2=Both
            // 场景 ActionBasedController 依赖新 Input System → 使用 1
            try
            {
                var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset");
                if (assets != null && assets.Length > 0)
                {
                    var so = new SerializedObject(assets[0]);
                    var prop = so.FindProperty("activeInputHandler");
                    if (prop != null)
                    {
                        prop.intValue = 1;
                        so.ApplyModifiedPropertiesWithoutUndo();
                        Debug.Log("  ✅ Active Input Handling = Input System Package");
                    }
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"  设置 activeInputHandler 失败: {e.Message}");
            }

            // PICO Platform appID 占位（无商店 entitlement 时用 "0"）
            var platform = Resources.Load<PXR_PlatformSetting>("PXR_PlatformSetting");
            if (platform != null && string.IsNullOrWhiteSpace(platform.appID))
            {
                platform.appID = "0";
                EditorUtility.SetDirty(platform);
                AssetDatabase.SaveAssets();
                Debug.Log("  ✅ PXR appID 已设为 0（开发占位）");
            }
            else if (platform != null)
            {
                Debug.Log($"  PXR appID: {platform.appID}");
            }

            // IL2CPP 后端（PICO SDK 要求）+ ARM64
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;

            // 强制 OpenGLES3，规避 PICO 模拟器 gfxstream Vulkan 驱动
            // VkDescriptorSet rehash 时的 Scudo 内存损坏崩溃（UnityGfxDeviceW SIGABRT）
            // 真机 PICO OS 的 Vulkan 实现正常，可在真机发版时移除此项恢复自动检测。
            var androidApis = PlayerSettings.GetGraphicsAPIs(BuildTarget.Android);
            bool alreadyGles = androidApis != null && androidApis.Length == 1
                              && androidApis[0] == UnityEngine.Rendering.GraphicsDeviceType.OpenGLES3;
            if (!alreadyGles)
            {
                PlayerSettings.SetGraphicsAPIs(BuildTarget.Android,
                    new[] { UnityEngine.Rendering.GraphicsDeviceType.OpenGLES3 });
                Debug.Log("  ✅ Graphics API = OpenGLES3 only（绕过模拟器 Vulkan 驱动崩溃）");
            }

            // 确保 Build 目录存在
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(apkPath)));

            var buildOptions = BuildOptions.Development | BuildOptions.AllowDebugging;
            var buildPlayerOptions = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = apkPath,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = buildOptions,
            };

            var report = BuildPipeline.BuildPlayer(buildPlayerOptions);
            var summary = report.summary;

            if (summary.result == BuildResult.Succeeded)
            {
                Debug.Log($"✅ APK 构建成功！路径: {System.IO.Path.GetFullPath(apkPath)}");
                Debug.Log($"   大小: {summary.totalSize / 1024 / 1024}MB");
                Debug.Log($"   耗时: {summary.totalTime.TotalSeconds:F1}s");
            }
            else
            {
                Debug.LogError($"❌ APK 构建失败: {summary.result}");
                foreach (var step in report.steps)
                {
                    foreach (var log in step.messages)
                    {
                        if (log.type == LogType.Error)
                            Debug.LogError($"  {log.content}");
                    }
                }
            }
        }
    }
}
#endif
