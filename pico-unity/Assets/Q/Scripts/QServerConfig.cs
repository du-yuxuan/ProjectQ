// ============================================================
// QServerConfig.cs
// Q (Cue) — 服务器地址配置（ScriptableObject）
//
// 使用方法：在 Unity Project 窗口中右键 → Create → Q → Server Config
// 将生成的 asset 拖到 QWebSocketClient 的 ServerConfig 字段即可
// ============================================================

using UnityEngine;

namespace Q.Pico
{
    /// <summary>
    /// 服务器连接配置。统一管理开发/生产环境地址。
    /// </summary>
    [CreateAssetMenu(fileName = "QServerConfig", menuName = "Q/Server Config", order = 1)]
    public class QServerConfig : ScriptableObject
    {
        [Header("环境选择")]
        [Tooltip("切换开发/生产环境")]
        public ServerEnvironment environment = ServerEnvironment.Development;

        [Header("开发环境")]
        [Tooltip("本地开发服务器地址")]
        public string devWsUrl = "ws://localhost:3001/ws/session";

        [Header("生产环境 (Zeabur)")]
        [Tooltip("Zeabur 部署的 WebSocket 地址")]
        public string prodWsUrl = "wss://qqqq.preview.aliyun-zeabur.cn/ws/session";

        [Header("自定义")]
        [Tooltip("手动覆盖地址（优先级最高）")]
        public string customWsUrl = "";

        /// <summary>当前生效的 WebSocket 地址</summary>
        public string ResolvedWsUrl
        {
            get
            {
                if (!string.IsNullOrEmpty(customWsUrl))
                    return customWsUrl;
                return environment == ServerEnvironment.Production ? prodWsUrl : devWsUrl;
            }
        }

        /// <summary>当前生效的 HTTP REST API 基础地址（用于非 WebSocket 调用）</summary>
        public string ResolvedHttpBaseUrl
        {
            get
            {
                string wsUrl = ResolvedWsUrl;
                // ws:// → http://, wss:// → https://
                string httpUrl = wsUrl
                    .Replace("wss://", "https://")
                    .Replace("ws://", "http://");
                // 去掉 /ws/session 后缀
                int idx = httpUrl.LastIndexOf("/ws/");
                return idx > 0 ? httpUrl.Substring(0, idx) : httpUrl;
            }
        }
    }

    /// <summary>服务器环境</summary>
    public enum ServerEnvironment
    {
        Development = 0,
        Production = 1,
    }
}
