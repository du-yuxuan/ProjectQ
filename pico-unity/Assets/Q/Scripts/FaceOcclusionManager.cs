// ============================================================
// FaceOcclusionManager.cs
// Q (Cue) — 人头遮挡与识别（遮脸减压核心）
//
// 使用 PICO Unity Integration SDK 的 SecureMR C# API 实现。
// 基于 SecureMR-Samples UFO 示例的 4 管线架构：
//   1. VST 管线：RectifiedVstAccessOperator → 获取左右眼相机画面
//   2. 推理管线：RunModelInferenceOperator → MediaPipe 人脸检测 → 2D 坐标
//   3. 2D→3D 管线：UvTo3DInCameraSpaceOperator + CameraSpaceToWorldOperator → 3D 位置
//   4. 渲染管线：SwitchGltfRenderStatusOperator → 在检测到的人脸位置渲染物种化身
//
// 参考：
//   - SecureMR C# API 文档：https://developer-cn.picoxr.com/document/unity-integration/securemr-overview
//   - UFO 示例（C++ 原生版）：https://github.com/Pico-Developer/SecureMR-Samples/samples/ufo
//   - SecureMR Unity 示例：https://github.com/Pico-Developer/SecureMR-Unity-Sample
// ============================================================

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json;
using Unity.XR.PXR;
using Unity.XR.PXR.SecureMR;
using Color = Unity.XR.PXR.SecureMR.Color;

namespace Q.Pico
{
    // ============================================================
    // 人脸检测结果数据结构
    // ============================================================

    /// <summary>
    /// 单张检测到的人脸（用于 Unity 侧的化身跟随和观众反馈）。
    /// 3D 坐标来自 SecureMR 的 UvTo3DInCameraSpaceOperator + CameraSpaceToWorldOperator。
    /// </summary>
    [Serializable]
    public struct DetectedFace
    {
        public int trackId;
        public Vector3 worldPosition;
        public Quaternion worldRotation;
        public float confidence;
        public float worldWidth;
        public string expression;
    }

    /// <summary>
    /// 人头遮挡管理器 — 使用 PICO SecureMR C# API 实现人脸检测 + 物种化身渲染。
    ///
    /// 架构（4 条 SecureMR Pipeline，与 UFO 示例对齐）：
    ///   Pipeline 1 (VST)：RectifiedVstAccessOperator → 全局 tensor: leftImage, rightImage, timestamp, cameraMatrix
    ///   Pipeline 2 (推理)：RunModelInferenceOperator(facedetector) → 全局 tensor: uv(2D人脸中心), isFaceDetected
    ///   Pipeline 3 (2D→3D)：UvTo3DInCameraSpaceOperator + CameraSpaceToWorldOperator → 全局 tensor: currentPosition(4x4矩阵)
    ///   Pipeline 4 (渲染)：ArithmeticComposeOperator(阻尼平滑) + SwitchGltfRenderStatusOperator → 渲染物种化身
    /// </summary>
    public class FaceOcclusionManager : MonoBehaviour
    {
        [Header("SecureMR 配置")]
        [Tooltip("VST 图像宽度（与 Provider 创建时一致）")]
        public int vstImageWidth = 256;
        [Tooltip("VST 图像高度")]
        public int vstImageHeight = 256;
        [Tooltip("人脸检测模型文件（MediaPipe Face Detection, Qualcomm AI Hub QNN 格式）\n放在 StreamingAssets 目录下")]
        public string faceDetectionModelPath = "facedetector_fp16_qnn229.bin";
        [Tooltip("Anchor 矩阵文件（人脸检测预定义锚点）")]
        public string anchorMatrixPath = "anchors_1.mat";

        [Header("物种化身 Prefab（Unity 侧渲染备份，SecureMR 侧用 glTF）")]
        [Tooltip("老虎 Prefab")]
        public GameObject tigerPrefab;
        [Tooltip("兔子 Prefab")]
        public GameObject rabbitPrefab;
        [Tooltip("猫头鹰 Prefab")]
        public GameObject owlPrefab;
        [Tooltip("狐狸 Prefab")]
        public GameObject foxPrefab;
        [Tooltip("狮子 Prefab")]
        public GameObject lionPrefab;
        [Tooltip("狼 Prefab")]
        public GameObject wolfPrefab;
        [Tooltip("鹿 Prefab（中性/默认）")]
        public GameObject deerPrefab;

        [Header("遮脸减压参数")]
        [Tooltip("化身跟随平滑系数（0-1，越大越平滑）")]
        public float dampingFactor = 0.95f;
        [Tooltip("化身缩放")]
        public float avatarScale = 0.1f;
        [Tooltip("化身高度偏移（米，在人脸上方）")]
        public float heightOffset = 0.25f;

        [Header("观众反馈")]
        [Tooltip("是否向后端发送观众反馈数据")]
        public bool sendAudienceFeedback = true;
        [Tooltip("观众反馈发送间隔（秒）")]
        public float feedbackInterval = 1.0f;

        [Header("调试")]
        [Tooltip("在 Editor 中使用 Mock 数据（无 SecureMR 时）")]
        public bool useMockInEditor = true;
        [Tooltip("启用 SecureMR 人脸检测")]
        public bool enableSecureMR = false;

        /// <summary>SecureMR 是否已初始化并在跑。</summary>
        public bool IsRunning => isInitialized && isRunning;

        /// <summary>当前是否应渲染冬瓜遮挡（由设置「屏蔽听众」控制）。</summary>
        public bool OccludeAudienceFaces { get; private set; }

        // ============================================================
        // SecureMR 核心对象
        // ============================================================

        /// <summary>SecureMR Provider（应用与 SecureMR 服务的会话）</summary>
        private Provider secureMRProvider;

        // 4 条 Pipeline
        private Pipeline vstPipeline;       // 管线 1：VST 相机画面获取
        private Pipeline inferencePipeline;  // 管线 2：人脸检测推理
        private Pipeline map2dTo3dPipeline;  // 管线 3：2D→3D 投影
        private Pipeline renderPipeline;     // 管线 4：化身渲染

        // ============================================================
        // 全局 Tensor（跨 Pipeline 共享）
        // ============================================================

        // VST 管线输出
        private Tensor globalLeftImageUint8;    // 左眼 RGB 图像 (UInt8, 3ch, 256x256)
        private Tensor globalRightImageUint8;   // 右眼 RGB 图像
        private Tensor globalLeftImageFp32;     // 左眼图像 Float32 版（推理用）
        private Tensor globalTimeStamp;         // 相机时间戳
        private Tensor globalCameraMatrix;     // 相机内参矩阵 (3x3)

        // 推理管线输出
        private Tensor globalUv;               // 人脸 2D 中心坐标 (Point2, Int32)
        private Tensor globalIsFaceDetected;   // 是否检测到人脸 (Scalar, Int8)

        // 2D→3D 管线输出
        private Tensor globalCurrentPosition;   // 当前 3D 位置 (4x4 矩阵, Float32)
        private Tensor globalPreviousPosition;  // 上一帧位置（阻尼用）

        // 渲染管线
        private Tensor globalGltfAsset;         // glTF 物种化身资产

        // ============================================================
        // Pipeline 内部 Tensor（Placeholder + Local）
        // ============================================================

        // VST 管线 Placeholders
        private Tensor phLeftImageUint8;
        private Tensor phRightImageUint8;
        private Tensor phLeftImageFp32;
        private Tensor phTimeStamp;
        private Tensor phCameraMatrix;

        // 推理管线 Placeholders
        private Tensor phVstImage;
        private Tensor phUv;
        private Tensor phIsFaceDetected;

        // 2D→3D 管线 Placeholders
        private Tensor phUv2;
        private Tensor phTimeStamp2;
        private Tensor phCameraMatrix2;
        private Tensor phLeftImage2;
        private Tensor phRightImage2;
        private Tensor phCurrentPosition;

        // 渲染管线 Placeholders
        private Tensor phGltf;
        private Tensor phPreviousPosition;
        private Tensor phCurrentPosition2;

        // ============================================================
        // 运行状态
        // ============================================================

        private bool isInitialized = false;
        private bool isRunning = false;
        private byte[] modelData;
        private byte[] anchorData;
        private float feedbackTimer = 0f;

        // 当前物种类型（来自后端 species_update 消息）
        private string currentSpecies = "deer";

        // ============================================================
        // Unity 生命周期
        // ============================================================

        void Start()
        {
#if UNITY_EDITOR
            if (useMockInEditor)
            {
                Debug.Log("[FaceOcclusion] Editor 模式，跳过 SecureMR 初始化");
                isInitialized = false;
                return;
            }
#endif
            // 默认关闭 SecureMR，避免模拟器/未配好模型时刷屏；需要时在 Inspector 勾选 enableSecureMR
            if (!enableSecureMR)
            {
                Debug.Log("[FaceOcclusion] SecureMR 已禁用（enableSecureMR=false），仅运行 HUD");
                isInitialized = false;
                return;
            }
            StartCoroutine(InitializeSecureMR());
        }

        void OnDestroy()
        {
            Shutdown();
        }

        void Update()
        {
            if (!isInitialized || !isRunning) return;

            // 执行 4 条管线（按依赖顺序），每步独立捕获异常并记录
            try { RunVstPipeline(); }
            catch (System.Exception e) { LogPipelineError("VST", e); return; }

            try { RunInferencePipeline(); }
            catch (System.Exception e) { LogPipelineError("Inference", e); return; }

            try { RunMap2dTo3dPipeline(); }
            catch (System.Exception e) { LogPipelineError("Map2dTo3d", e); return; }

            try { RunRenderPipeline(); }
            catch (System.Exception e) { LogPipelineError("Render", e); return; }

            // 观众反馈定时发送
            if (sendAudienceFeedback)
            {
                feedbackTimer += Time.deltaTime;
                if (feedbackTimer >= feedbackInterval)
                {
                    feedbackTimer = 0f;
                    SendAudienceFeedbackToBackend();
                }
            }
        }
        private bool suppressUpdateError;

        // ============================================================
        // SecureMR 初始化
        // ============================================================

        /// <summary>
        /// 初始化 SecureMR：加载模型 → 创建 Provider → 创建全局 Tensor → 创建 4 条 Pipeline
        /// </summary>
        IEnumerator InitializeSecureMR()
        {
            Debug.Log("[FaceOcclusion] InitializeSecureMR 开始...");

            // 1. 加载人脸检测模型（从 Resources，编译进 APK，零网络开销）
            var modelAsset = Resources.Load<TextAsset>("FaceDetection/facedetector_fp16_qnn229");
            if (modelAsset == null)
            {
                Debug.LogError("[FaceOcclusion] 人脸检测模型 Resources 加载失败");
                yield break;
            }
            modelData = modelAsset.bytes;
            Debug.Log($"[FaceOcclusion] 人脸检测模型已加载: {modelData.Length} bytes");

            // 2. 加载 Anchor 矩阵
            var anchorAsset = Resources.Load<TextAsset>("FaceDetection/anchors_1");
            anchorData = anchorAsset != null ? anchorAsset.bytes : null;

            // 3. 开启视频透视
            PXR_Manager.EnableVideoSeeThrough = true;

            // 4. 创建 Provider
            secureMRProvider = new Provider(vstImageWidth, vstImageHeight);
            Debug.Log($"[FaceOcclusion] Provider 已创建 ({vstImageWidth}x{vstImageHeight})");

            // 5. 创建全局 Tensor
            try { CreateGlobalTensors(); Debug.Log("[FaceOcclusion] 全局 Tensor 已创建"); }
            catch (System.Exception e) { Debug.LogError($"[FaceOcclusion] Tensor 创建失败: {e.Message}"); yield break; }

            // 6. 创建 4 条 Pipeline
            try { CreateVstPipeline(); Debug.Log("[FaceOcclusion] VST 管线已创建"); }
            catch (System.Exception e) { Debug.LogError($"[FaceOcclusion] VST 管线创建失败: {e.Message}"); yield break; }

            try { CreateInferencePipeline(); Debug.Log("[FaceOcclusion] Inference 管线已创建"); }
            catch (System.Exception e) { Debug.LogError($"[FaceOcclusion] Inference 管线创建失败: {e.Message}"); yield break; }

            try { CreateMap2dTo3dPipeline(); Debug.Log("[FaceOcclusion] Map2dTo3d 管线已创建"); }
            catch (System.Exception e) { Debug.LogError($"[FaceOcclusion] Map2dTo3d 管线创建失败: {e.Message}"); yield break; }

            try { CreateRenderPipeline(); Debug.Log("[FaceOcclusion] Render 管线已创建"); }
            catch (System.Exception e) { Debug.LogError($"[FaceOcclusion] Render 管线创建失败: {e.Message}"); yield break; }

            isInitialized = true;
            // isRunning 延后到 glTF 加载完成再设为 true，避免在 yield 间隙
            // Update() 执行 Render 管线时 globalGltfAsset 仍为 null 导致报错。

            // 7. 加载 glTF 冬瓜模型（连同 scene.bin 和纹理一起缓存）
            string gltfRelPath = "species/deer/scene.gltf";
            string binRelPath = "species/deer/scene.bin";
            yield return EnsureFileInPersistentPath(gltfRelPath);
            yield return EnsureFileInPersistentPath(binRelPath);
            byte[] gltfData = ReadFromPersistentPath(gltfRelPath);
            if (gltfData != null)
            {
                try
                {
                    globalGltfAsset = secureMRProvider.CreateTensor<Gltf>(gltfData);
                    Debug.Log($"[FaceOcclusion] glTF 冬瓜模型加载完成 ({gltfData.Length} bytes)");
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[FaceOcclusion] glTF 加载失败: {e.Message}");
                }
            }
            else
            {
                Debug.LogError("[FaceOcclusion] glTF 冬瓜模型持久化缓存不存在，无法渲染。请确保 StreamingAssets/species/deer/scene.gltf + scene.bin 存在");
            }

            // glTF 加载完成后才开启 Update 管线执行
            isRunning = true;

            Debug.Log("[FaceOcclusion] SecureMR 4 管线初始化完成");
        }

        // ============================================================
        // 全局 Tensor 创建
        // ============================================================

        void CreateGlobalTensors()
        {
            // VST 图像（UInt8, 3-channel RGB）
            globalLeftImageUint8 = secureMRProvider.CreateTensor<byte, Matrix>(3, new TensorShape(vstImageHeight, vstImageWidth));
            globalRightImageUint8 = secureMRProvider.CreateTensor<byte, Matrix>(3, new TensorShape(vstImageHeight, vstImageWidth));
            // Float32 版（推理需要归一化到 0-1）
            globalLeftImageFp32 = secureMRProvider.CreateTensor<float, Matrix>(3, new TensorShape(vstImageHeight, vstImageWidth));
            // 时间戳
            globalTimeStamp = secureMRProvider.CreateTensor<int, TimeStamp>(4, new TensorShape(1));
            // 相机内参矩阵
            globalCameraMatrix = secureMRProvider.CreateTensor<float, Matrix>(1, new TensorShape(3, 3));

            // 人脸检测结果
            // UV 坐标 (2D 人脸中心)：2-channel Int32 Point, shape (1,)
            globalUv = secureMRProvider.CreateTensor<int, Point>(2, new TensorShape(1));
            // 是否检测到人脸：1-channel Int8 Scalar, shape (1,)
            globalIsFaceDetected = secureMRProvider.CreateTensor<sbyte, Scalar>(1, new TensorShape(1));

            // 3D 位置（4x4 变换矩阵，Float32）— 初始化为单位矩阵
            float[] identity = {
                1, 0, 0, 0,
                0, 1, 0, 0,
                0, 0, 1, 0,
                0, 0, 0, 1
            };
            globalCurrentPosition = secureMRProvider.CreateTensor<float, Matrix>(1, new TensorShape(4, 4), identity);
            globalPreviousPosition = secureMRProvider.CreateTensor<float, Matrix>(1, new TensorShape(4, 4), identity);

            // glTF 物种化身（默认加载鹿，可动态切换）
            // 实际使用时从 StreamingAssets 加载对应物种的 glTF 文件
            // globalGltfAsset = secureMRProvider.CreateTensor<Gltf>(gltfData);
            // 暂时留空，等加载 glTF 后赋值
        }

        // ============================================================
        // Pipeline 1: VST 相机画面获取
        // ============================================================

        /// <summary>
        /// 创建 VST 管线：
        /// RectifiedVstAccessOperator → 左右眼图像 + 时间戳 + 相机内参
        /// ArithmeticComposeOperator → UInt8 转 Float32 (÷255.0)
        /// </summary>
        void CreateVstPipeline()
        {
            vstPipeline = secureMRProvider.CreatePipeline();

            // Placeholders（引用全局 Tensor）
            phLeftImageUint8 = vstPipeline.CreateTensorReference<byte, Matrix>(3, new TensorShape(vstImageHeight, vstImageWidth));
            phRightImageUint8 = vstPipeline.CreateTensorReference<byte, Matrix>(3, new TensorShape(vstImageHeight, vstImageWidth));
            phLeftImageFp32 = vstPipeline.CreateTensorReference<float, Matrix>(3, new TensorShape(vstImageHeight, vstImageWidth));
            phTimeStamp = vstPipeline.CreateTensorReference<int, TimeStamp>(4, new TensorShape(1));
            phCameraMatrix = vstPipeline.CreateTensorReference<float, Matrix>(1, new TensorShape(3, 3));

            // Operator 1: 获取 VST 相机画面
            var vstOp = vstPipeline.CreateOperator<RectifiedVstAccessOperator>();
            vstOp.SetResult("left image", phLeftImageUint8);
            vstOp.SetResult("right image", phRightImageUint8);
            vstOp.SetResult("timestamp", phTimeStamp);
            vstOp.SetResult("camera matrix", phCameraMatrix);

            // Operator 2: UInt8 → Float32 归一化 (/ 255.0)
            var normalizeOp = vstPipeline.CreateOperator<ArithmeticComposeOperator>(
                new ArithmeticComposeOperatorConfiguration("{0} / 255.0"));
            normalizeOp.SetOperand("{0}", phLeftImageUint8);
            normalizeOp.SetResult("result", phLeftImageFp32);
        }

        // ============================================================
        // Pipeline 2: 人脸检测推理
        // ============================================================

        /// <summary>
        /// 创建推理管线：
        /// RunModelInferenceOperator(MediaPipe Face Detection) → 人脸 2D 坐标 + 置信度
        /// ArgmaxOperator → 找最高置信度的人脸
        /// CustomizedCompareOperator → 判断是否超过阈值
        /// AllOperator → 综合判断
        ///
        /// 模型：facedetector_fp16_qnn229.bin（Qualcomm AI Hub MediaPipe Face Detection）
        /// 输入：Float32 归一化的左眼图像
        /// 输出：face_anchor (896x4 边界框) + score (896 置信度)
        /// </summary>
        void CreateInferencePipeline()
        {
            inferencePipeline = secureMRProvider.CreatePipeline();

            // Placeholders
            phVstImage = inferencePipeline.CreateTensorReference<float, Matrix>(3, new TensorShape(vstImageHeight, vstImageWidth));
            phUv = inferencePipeline.CreateTensorReference<int, Point>(2, new TensorShape(1));
            phIsFaceDetected = inferencePipeline.CreateTensorReference<sbyte, Scalar>(1, new TensorShape(1));

            // Local Tensors
            // 模型输出：face_anchor (896 个边界框, 每个含 4 个坐标 + 4 个关键点 = 8 维) 和 score (896 个置信度)
            var faceAnchor = inferencePipeline.CreateTensor<float, Matrix>(1, new TensorShape(896, 16));
            var faceScores = inferencePipeline.CreateTensor<float, Scalar>(1, new TensorShape(896));

            // Anchor 矩阵（预定义锚点，用于将模型输出转换为绝对坐标）
            var anchorMat = inferencePipeline.CreateTensor<float, Matrix>(1, new TensorShape(896, 4));
            if (anchorData != null && anchorData.Length > 0)
            {
                // 从二进制文件加载 anchor 数据
                float[] anchorFloats = new float[anchorData.Length / 4];
                Buffer.BlockCopy(anchorData, 0, anchorFloats, 0, anchorData.Length);
                anchorMat.Reset(anchorFloats);
            }

            // 人脸关键点（从 anchor 中提取 4 个坐标）
            var faceLandmarks = inferencePipeline.CreateTensor<float, Matrix>(1, new TensorShape(896, 4));

            // 最佳人脸的索引
            var bestFaceIndex = inferencePipeline.CreateTensor<int, Matrix>(1, new TensorShape(1, 1));
            var bestFaceIndexPlusOne = inferencePipeline.CreateTensor<int, Matrix>(1, new TensorShape(1, 1));

            // 切片 tensor（用于从 896 个结果中提取最佳人脸）
            var bestFaceSrcSlice2 = inferencePipeline.CreateTensor<int, Slice>(2, new TensorShape(1));
            bestFaceSrcSlice2.Reset(new int[] { 0, -1, 0, 2 }); // [0:-1, 0:2]

            var bestFaceSrcSlice1 = inferencePipeline.CreateTensor<int, Slice>(2, new TensorShape(1));
            bestFaceSrcSlice1.Reset(new int[] { 0, -1 }); // [0:-1]

            // 最佳人脸置信度
            var bestFaceScore = inferencePipeline.CreateTensor<float, Scalar>(1, new TensorShape(1));

            // UV 阈值（过滤无效检测）
            var uvThreshold = inferencePipeline.CreateTensor<int, Point>(2, new TensorShape(1));
            uvThreshold.Reset(new int[] { 20, 20 });

            // 比较结果
            var uvDetected = inferencePipeline.CreateTensor<int, Point>(2, new TensorShape(1));
            var scoreDetected = inferencePipeline.CreateTensor<int, Scalar>(1, new TensorShape(1));
            var temp = inferencePipeline.CreateTensor<sbyte, Matrix>(1, new TensorShape(2, 1));

            // 配置模型输入输出
            var modelInput = new SecureMROperatorModelConfig
            {
                encodingType = SecureMRModelEncoding.Float32,
                nodeName = "input_1",
                operatorIOName = "image",
            };
            var modelOutputAnchor = new SecureMROperatorModelConfig
            {
                encodingType = SecureMRModelEncoding.Float32,
                nodeName = "face_anchor",
                operatorIOName = "face_anchor",
            };
            var modelOutputScore = new SecureMROperatorModelConfig
            {
                encodingType = SecureMRModelEncoding.Float32,
                nodeName = "score",
                operatorIOName = "score",
            };

            var inputConfigs = new List<SecureMROperatorModelConfig> { modelInput };
            var outputConfigs = new List<SecureMROperatorModelConfig> { modelOutputAnchor, modelOutputScore };

            // 创建推理 Operator
            var modelConfig = new ModelOperatorConfiguration(
                inputConfigs, outputConfigs, modelData,
                SecureMRModelType.QnnContextBinary, "face");
            var modelOp = inferencePipeline.CreateOperator<RunModelInferenceOperator>(modelConfig);

            // 后处理：提取最佳人脸
            // 1. 从 anchor 中提取坐标 ([:, 4:8] → faceLandmarks)
            var assignAnchor = inferencePipeline.CreateOperator<AssignmentOperator>();
            var anchorSlice = inferencePipeline.CreateTensor<int, Slice>(2, new TensorShape(1));
            anchorSlice.Reset(new int[] { 0, -1, 4, 8 });
            assignAnchor.SetOperand("src", anchorMat);
            assignAnchor.SetOperand("src slices", anchorSlice);
            assignAnchor.SetResult("dst", faceLandmarks);

            // 2. 坐标 = (faceLandmarks / 256.0 + anchorMat) * 256.0（归一化→绝对坐标）
            var arithOp = inferencePipeline.CreateOperator<ArithmeticComposeOperator>(
                new ArithmeticComposeOperatorConfiguration("({0} / 256.0 + {1}) * 256.0"));
            arithOp.SetOperand("{0}", faceLandmarks);
            arithOp.SetOperand("{1}", anchorMat);
            arithOp.SetResult("result", faceLandmarks);

            // 3. 找最高置信度的人脸
            var argmaxOp = inferencePipeline.CreateOperator<ArgmaxOperator>();
            argmaxOp.SetOperand("operand", faceScores);
            argmaxOp.SetResult("result", bestFaceIndex);

            // 4. 索引 +1（用于切片）
            var arithPlusOne = inferencePipeline.CreateOperator<ArithmeticComposeOperator>(
                new ArithmeticComposeOperatorConfiguration("({0} + 1)"));
            arithPlusOne.SetOperand("{0}", bestFaceIndex);
            arithPlusOne.SetResult("result", bestFaceIndexPlusOne);

            // 5. 将最佳人脸的坐标和置信度提取出来
            var assignBest = inferencePipeline.CreateOperator<AssignmentOperator>();
            assignBest.SetOperand("src", bestFaceIndex);
            assignBest.SetOperand("dst slices", bestFaceSrcSlice2);
            assignBest.SetResult("dst", faceLandmarks);

            // 6. 提取最佳人脸的 UV 坐标
            var assignUv = inferencePipeline.CreateOperator<AssignmentOperator>();
            var faceLandmarkSlice = inferencePipeline.CreateTensor<int, Slice>(2, new TensorShape(1));
            faceLandmarkSlice.Reset(new int[] { 0, -1, 0, 2 });
            assignUv.SetOperand("src", faceLandmarks);
            assignUv.SetOperand("src slices", faceLandmarkSlice);
            assignUv.SetResult("dst", phUv);

            // 7. 提取最佳人脸的置信度
            var assignScore = inferencePipeline.CreateOperator<AssignmentOperator>();
            assignScore.SetOperand("src", faceScores);
            assignScore.SetOperand("src slices", bestFaceSrcSlice1);
            assignScore.SetResult("dst", bestFaceScore);

            // 8. 判断置信度是否超过阈值 (0.55)
            var thresholdTensor = inferencePipeline.CreateTensor<float, Scalar>(1, new TensorShape(1));
            thresholdTensor.Reset(new float[] { 0.55f });

            var compareScore = inferencePipeline.CreateOperator<CustomizedCompareOperator>(
                new ComparisonOperatorConfiguration(SecureMRComparison.LargerThan));
            compareScore.SetOperand("operand0", bestFaceScore);
            compareScore.SetOperand("operand1", thresholdTensor);
            compareScore.SetResult("result", scoreDetected);

            // 9. 判断 UV 是否有效
            var compareUv = inferencePipeline.CreateOperator<CustomizedCompareOperator>(
                new ComparisonOperatorConfiguration(SecureMRComparison.LargerThan));
            compareUv.SetOperand("operand0", phUv);
            compareUv.SetOperand("operand1", uvThreshold);
            compareUv.SetResult("result", uvDetected);

            // 10. AND 运算：UV 有效 AND 置信度足够 → isFaceDetected
            var andOp = inferencePipeline.CreateOperator<ElementwiseAndOperator>();
            andOp.SetOperand("operand0", uvDetected);
            andOp.SetOperand("operand1", scoreDetected);
            andOp.SetResult("result", temp);

            var allOp = inferencePipeline.CreateOperator<AllOperator>();
            allOp.SetOperand("operand", temp);
            allOp.SetResult("result", phIsFaceDetected);
        }

        // ============================================================
        // Pipeline 3: 2D → 3D 投影
        // ============================================================

        /// <summary>
        /// 创建 2D→3D 管线：
        /// UvTo3DInCameraSpaceOperator → 2D UV 投影到相机坐标系 3D 坐标（使用深度传感器）
        /// CameraSpaceToWorldOperator → 相机坐标系 → OpenXR Local 世界坐标系
        /// ArithmeticComposeOperator → 坐标轴翻转 + 偏移
        /// </summary>
        void CreateMap2dTo3dPipeline()
        {
            map2dTo3dPipeline = secureMRProvider.CreatePipeline();

            // Placeholders
            phUv2 = map2dTo3dPipeline.CreateTensorReference<int, Point>(2, new TensorShape(1));
            phTimeStamp2 = map2dTo3dPipeline.CreateTensorReference<int, TimeStamp>(4, new TensorShape(1));
            phCameraMatrix2 = map2dTo3dPipeline.CreateTensorReference<float, Matrix>(1, new TensorShape(3, 3));
            phLeftImage2 = map2dTo3dPipeline.CreateTensorReference<byte, Matrix>(3, new TensorShape(vstImageHeight, vstImageWidth));
            phRightImage2 = map2dTo3dPipeline.CreateTensorReference<byte, Matrix>(3, new TensorShape(vstImageHeight, vstImageWidth));
            phCurrentPosition = map2dTo3dPipeline.CreateTensorReference<float, Matrix>(1, new TensorShape(4, 4));

            // Local Tensors
            // 3D 点坐标
            var pointXYZ = map2dTo3dPipeline.CreateTensor<float, Point>(3, new TensorShape(1));

            // 坐标轴乘数（翻转 Y 轴：PICO 坐标系 Y 向下，OpenXR Y 向上）
            var pointMultiplier = map2dTo3dPipeline.CreateTensor<float, Matrix>(1, new TensorShape(3, 1));
            pointMultiplier.Reset(new float[] { 1.0f, -1.0f, 1.0f });

            // 偏移量（化身悬浮在人脸上方）
            var offset = map2dTo3dPipeline.CreateTensor<float, Matrix>(1, new TensorShape(3, 1));
            offset.Reset(new float[] { 0.1f, heightOffset, -0.05f });

            // 旋转、平移、缩放（构建 4x4 变换矩阵）
            var rvec = map2dTo3dPipeline.CreateTensor<float, Matrix>(1, new TensorShape(3, 1));
            rvec.Reset(new float[] { 0, 0, 0 });
            var svec = map2dTo3dPipeline.CreateTensor<float, Matrix>(1, new TensorShape(3, 1));
            svec.Reset(new float[] { avatarScale, avatarScale, avatarScale });

            // 左眼相机到世界的变换矩阵
            var leftEyeTransform = map2dTo3dPipeline.CreateTensor<float, Matrix>(1, new TensorShape(4, 4));

            // Operator 1: UV → 相机坐标系 3D 点
            var uv2camOp = map2dTo3dPipeline.CreateOperator<UvTo3DInCameraSpaceOperator>();
            uv2camOp.SetOperand("uv", phUv2);
            uv2camOp.SetOperand("timestamp", phTimeStamp2);
            // 注意：SDK 的 operand 名称为 "camera intrisic"（原文拼写错误，非 intrinsic）
            uv2camOp.SetOperand("camera intrisic", phCameraMatrix2);
            uv2camOp.SetOperand("left image", phLeftImage2);
            uv2camOp.SetOperand("right image", phRightImage2);
            uv2camOp.SetResult("point_xyz", pointXYZ);

            // Operator 2: 坐标轴翻转 (elementwise multiply)
            var multiplyOp = map2dTo3dPipeline.CreateOperator<ElementwiseMultiplyOperator>();
            multiplyOp.SetOperand("operand0", pointXYZ);
            multiplyOp.SetOperand("operand1", pointMultiplier);
            multiplyOp.SetResult("result", pointXYZ);

            // Operator 3: 加偏移量 (arithmetic)
            var addOffsetOp = map2dTo3dPipeline.CreateOperator<ArithmeticComposeOperator>(
                new ArithmeticComposeOperatorConfiguration("({0} + {1})"));
            addOffsetOp.SetOperand("{0}", pointXYZ);
            addOffsetOp.SetOperand("{1}", offset);
            addOffsetOp.SetResult("result", pointXYZ);

            // Operator 4: 构建变换矩阵 (rvec, tvec, svec → 4x4)
            // GetTransformMatrixOperator 对应 UFO 的 .transform() 操作
            var transformOp = map2dTo3dPipeline.CreateOperator<GetTransformMatrixOperator>();
            transformOp.SetOperand("rotation", rvec);
            transformOp.SetOperand("translation", pointXYZ);
            transformOp.SetOperand("scale", svec);
            transformOp.SetResult("result", phCurrentPosition);

            // Operator 5: 相机坐标系 → OpenXR Local 世界坐标系
            var cam2worldOp = map2dTo3dPipeline.CreateOperator<CameraSpaceToWorldOperator>();
            cam2worldOp.SetOperand("timestamp", phTimeStamp2);
            cam2worldOp.SetResult("left", leftEyeTransform);

            // Operator 6: 世界坐标 = 左眼变换 × 当前位置
            var worldTransformOp = map2dTo3dPipeline.CreateOperator<ArithmeticComposeOperator>(
                new ArithmeticComposeOperatorConfiguration("{0} * {1}"));
            worldTransformOp.SetOperand("{0}", leftEyeTransform);
            worldTransformOp.SetOperand("{1}", phCurrentPosition);
            worldTransformOp.SetResult("result", phCurrentPosition);
        }

        // ============================================================
        // Pipeline 4: 化身渲染（阻尼平滑 + glTF 渲染）
        // ============================================================

        /// <summary>
        /// 创建渲染管线：
        /// ArithmeticComposeOperator → 阻尼平滑（previousPos * 0.95 + currentPos * 0.05）
        /// SwitchGltfRenderStatusOperator → 在平滑位置渲染物种化身 glTF
        ///
        /// 注意：渲染在 SecureMR 服务端完成，Unity 侧无需实例化 GameObject。
        /// 但为了支持物种动态切换和弹幕定位，Unity 侧也维护化身位置。
        /// </summary>
        void CreateRenderPipeline()
        {
            renderPipeline = secureMRProvider.CreatePipeline();

            // Placeholders
            phPreviousPosition = renderPipeline.CreateTensorReference<float, Matrix>(1, new TensorShape(4, 4));
            phCurrentPosition2 = renderPipeline.CreateTensorReference<float, Matrix>(1, new TensorShape(4, 4));
            phGltf = renderPipeline.CreateTensorReference<Gltf>();

            // Local: 阻尼插值结果
            var interpolated = renderPipeline.CreateTensor<float, Matrix>(1, new TensorShape(4, 4));

            // Operator 1: 阻尼平滑 (EMA: prev * damping + curr * (1-damping))
            var dampingOp = renderPipeline.CreateOperator<ArithmeticComposeOperator>(
                new ArithmeticComposeOperatorConfiguration(
                    $"({{0}} * {dampingFactor} + {{1}} * {1.0f - dampingFactor})"));
            dampingOp.SetOperand("{0}", phPreviousPosition);
            dampingOp.SetOperand("{1}", phCurrentPosition2);
            dampingOp.SetResult("result", interpolated);

            // Operator 2: 更新 previousPosition = interpolated（下一帧用）
            var updatePrev = renderPipeline.CreateOperator<AssignmentOperator>();
            updatePrev.SetOperand("src", interpolated);
            updatePrev.SetResult("dst", phPreviousPosition);

            // Operator 3: 渲染 glTF 化身在插值位置
            var renderOp = renderPipeline.CreateOperator<SwitchGltfRenderStatusOperator>();
            renderOp.SetOperand("gltf", phGltf);
            renderOp.SetOperand("world pose", interpolated);
        }

        // ============================================================
        // Pipeline 执行
        // ============================================================

        /// <summary>执行 VST 管线（获取相机画面）</summary>
        void RunVstPipeline()
        {
            var mapping = vstPipeline.CreateTensorMapping();
            mapping.Set(phLeftImageUint8, globalLeftImageUint8);
            mapping.Set(phRightImageUint8, globalRightImageUint8);
            mapping.Set(phLeftImageFp32, globalLeftImageFp32);
            mapping.Set(phTimeStamp, globalTimeStamp);
            mapping.Set(phCameraMatrix, globalCameraMatrix);
            vstPipeline.Execute(mapping);
        }

        /// <summary>执行推理管线（人脸检测）</summary>
        void RunInferencePipeline()
        {
            var mapping = inferencePipeline.CreateTensorMapping();
            mapping.Set(phVstImage, globalLeftImageFp32);
            mapping.Set(phUv, globalUv);
            mapping.Set(phIsFaceDetected, globalIsFaceDetected);
            inferencePipeline.Execute(mapping);
        }

        /// <summary>执行 2D→3D 管线（UV → 世界坐标）</summary>
        void RunMap2dTo3dPipeline()
        {
            var mapping = map2dTo3dPipeline.CreateTensorMapping();
            mapping.Set(phUv2, globalUv);
            mapping.Set(phTimeStamp2, globalTimeStamp);
            mapping.Set(phCameraMatrix2, globalCameraMatrix);
            mapping.Set(phLeftImage2, globalLeftImageUint8);
            mapping.Set(phRightImage2, globalRightImageUint8);
            mapping.Set(phCurrentPosition, globalCurrentPosition);
            map2dTo3dPipeline.Execute(mapping);
        }

        /// <summary>执行渲染管线（阻尼 + glTF 渲染）</summary>
        void RunRenderPipeline()
        {
            // glTF 未就绪时跳过渲染管线，避免 SwitchGltfRenderStatusOperator 缺参报错
            if (globalGltfAsset == null) return;

            var mapping = renderPipeline.CreateTensorMapping();
            mapping.Set(phPreviousPosition, globalPreviousPosition);
            mapping.Set(phCurrentPosition2, globalCurrentPosition);
            mapping.Set(phGltf, globalGltfAsset);
            // 关键：与官方 UFODemo 一致——仅当 isFaceDetected 为真时才执行渲染管线，
            // 否则 glTF 会以默认单位矩阵在原点渲染，表现为"白块"。
            renderPipeline.ExecuteConditional(globalIsFaceDetected.TensorHandle, mapping);
        }

        // ============================================================
        // 物种化身管理
        // ============================================================

        /// <summary>
        /// 更新物种化身（从后端 species_update 消息触发）
        /// 在 SecureMR 中重新加载对应物种的 glTF 资产
        /// </summary>
        public void UpdateSpecies(string species)
        {
            if (string.IsNullOrEmpty(species)) species = "deer";
            if (species == currentSpecies && globalGltfAsset != null) return;
            currentSpecies = species;

            // SecureMR 未初始化时只记录物种，等初始化后再加载
            if (secureMRProvider == null)
            {
                Debug.Log($"[FaceOcclusion] 物种已记录: {species}（SecureMR 未就绪，稍后加载）");
                return;
            }

            // 从 persistentDataPath 读取（InitializeSecureMR 已预先缓存）
            string gltfPath = $"species/{species}/scene.gltf";
            byte[] gltfData = ReadFromPersistentPath(gltfPath);
            if (gltfData == null)
            {
                Debug.LogWarning($"[FaceOcclusion] glTF 本地缓存不存在: {gltfPath}，物种={species}");
                return;
            }

            try
            {
                if (globalGltfAsset != null)
                {
                    globalGltfAsset.Destroy();
                    globalGltfAsset = null;
                }
                globalGltfAsset = secureMRProvider.CreateTensor<Gltf>(gltfData);
                Debug.Log($"[FaceOcclusion] 物种化身已切换: {species} ({gltfData.Length} bytes)");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[FaceOcclusion] 加载 glTF 失败: {e.Message}");
            }
        }

        /// <summary>
        /// 设置是否用冬瓜遮挡听众人脸（设置页「屏蔽听众」）。
        /// </summary>
        public void SetAudienceOcclusion(bool enabled)
        {
            OccludeAudienceFaces = enabled;
            sendAudienceFeedback = !enabled;
            if (enabled)
            {
                UpdateSpecies("deer");
#if UNITY_EDITOR
                if (useMockInEditor)
                {
                    Debug.Log("[FaceOcclusion] Editor mock 模式，跳过 SecureMR 初始化");
                }
                else
#endif
                {
                    // 每次开启都确保 SecureMR 已初始化（不受 enableSecureMR 旧状态影响）
                    enableSecureMR = true;
                    if (!isInitialized)
                        StartCoroutine(InitializeSecureMR());
                }
                Debug.Log("[FaceOcclusion] 听众遮挡 ON → 冬瓜(deer)");
            }
            else
            {
                Debug.Log("[FaceOcclusion] 听众遮挡 OFF");
                Shutdown();
            }

            var donggua = FindObjectOfType<DongguaOcclusionController>();
            if (donggua != null)
                donggua.SetOcclusionEnabled(enabled);
        }

        // ============================================================
        // 观众反馈（发送到后端）
        // ============================================================

        /// <summary>
        /// 向后端发送观众反馈数据（检测到的人脸数 = 观众数，全部视为"专注"）
        /// 实际产品中可通过表情分析区分专注/走神
        /// </summary>
        void SendAudienceFeedbackToBackend()
        {
            var wsClient = FindObjectOfType<QWebSocketClient>();
            if (wsClient == null) return;

            // 当前简化版：检测到人脸 = 专注，未检测到 = 走神
            // 实际产品中需要在推理管线中增加表情分类 operator
            int faceCount = 1; // 当前只跟踪一个人脸
            int attentive = 1;
            int distracted = 0;

            float ts = Time.realtimeSinceStartup;
            wsClient.SendAudienceFeedback(ts, faceCount, attentive, distracted);
        }

        // ============================================================
        // 清理
        // ============================================================

        void LogPipelineError(string pipeName, System.Exception e)
        {
            Debug.LogError($"[FaceOcclusion] {pipeName} 管线执行失败: {e.Message}\n{e.StackTrace}");
            if (!suppressUpdateError)
                isRunning = false;
        }

        void Shutdown()
        {
            isRunning = false;

            // 关闭视频透视，恢复普通渲染
            PXR_Manager.EnableVideoSeeThrough = false;

            if (globalGltfAsset != null)
            {
                globalGltfAsset.Destroy();
                globalGltfAsset = null;
            }

            // 销毁 Provider 会自动清理所有 Pipeline 和 Tensor
            if (secureMRProvider != null)
            {
                secureMRProvider.Destroy();
                secureMRProvider = null;
            }

            isInitialized = false;
            Debug.Log("[FaceOcclusion] SecureMR 已关闭");
        }

        // ============================================================
        // 文件加载：StreamingAssets → persistentDataPath（避免 Android jar: 协议失败）
        // ============================================================

        /// <summary>
        /// 确保 relativePath 文件在 persistentDataPath 中存在。
        /// 若不存在，从 StreamingAssets 复制。
        /// </summary>
        static System.Collections.IEnumerator EnsureFileInPersistentPath(string relativePath)
        {
            string destPath = System.IO.Path.Combine(Application.persistentDataPath, relativePath);
            if (System.IO.File.Exists(destPath))
                yield break; // 已缓存

            // 确保目录存在
            string destDir = System.IO.Path.GetDirectoryName(destPath);
            if (!string.IsNullOrEmpty(destDir) && !System.IO.Directory.Exists(destDir))
                System.IO.Directory.CreateDirectory(destDir);

#if UNITY_ANDROID && !UNITY_EDITOR
            // Android: 尝试直接 File.ReadAllBytes（部分设备 / 未压缩文件可用）
            string srcPath = System.IO.Path.Combine(Application.streamingAssetsPath, relativePath);
            byte[] data = null;
            try { data = System.IO.File.ReadAllBytes(srcPath); }
            catch (System.Exception) { /* APK 内不支持直接读，走 UnityWebRequest */ }

            if (data != null)
            {
                System.IO.File.WriteAllBytes(destPath, data);
                Debug.Log($"[FaceOcclusion] 已缓存到本地: {relativePath} ({data.Length} bytes)");
                yield break;
            }

            // 回退：UnityWebRequest 从 APK 内读取
            using (var request = UnityEngine.Networking.UnityWebRequest.Get(srcPath))
            {
                yield return request.SendWebRequest();
                if (request.result == UnityEngine.Networking.UnityWebRequest.Result.Success)
                {
                    System.IO.File.WriteAllBytes(destPath, request.downloadHandler.data);
                    Debug.Log($"[FaceOcclusion] 已缓存到本地: {relativePath} ({request.downloadHandler.data.Length} bytes)");
                }
                else
                {
                    Debug.LogError($"[FaceOcclusion] 从 APK 读取失败: {relativePath} → {request.error}");
                }
            }
#else
            // Editor / Standalone: 直接文件复制
            string srcPath = System.IO.Path.Combine(Application.streamingAssetsPath, relativePath);
            if (System.IO.File.Exists(srcPath))
            {
                System.IO.File.Copy(srcPath, destPath, true);
                Debug.Log($"[FaceOcclusion] 已缓存到本地: {relativePath}");
            }
            else
            {
                Debug.LogWarning($"[FaceOcclusion] 文件不存在: {srcPath}");
            }
            yield break;
#endif
        }

        /// <summary>从 persistentDataPath 读取文件。</summary>
        static byte[] ReadFromPersistentPath(string relativePath)
        {
            string path = System.IO.Path.Combine(Application.persistentDataPath, relativePath);
            if (System.IO.File.Exists(path))
                return System.IO.File.ReadAllBytes(path);
            return null;
        }
    }
}
