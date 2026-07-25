// Unfreeze — 讯飞实时语音转写大模型 WebSocket 客户端
// 协议：wss://office-api-ast-dx.iflyaisol.com/ast/communicate/v1?{params}
// 鉴权：HmacSHA1(accessKeySecret, baseString) → Base64
// 音频：16kHz 16bit mono PCM，每 40ms 发 1280 字节

import WebSocket from 'ws';
import crypto from 'crypto';
import http from 'http';
import tls from 'tls';
import { config } from '../config.js';
import { SpeakerSmoother } from './speaker-smoother.js';

export interface IflytekAsrResult {
  text: string;
  isFinal: boolean;
  ts: number;
  /** 说话人 ID（1, 2, 3...），未开启分离时为 0 */
  speaker: number;
}

type OnAsrCallback = (result: IflytekAsrResult) => void;
type OnStatusCallback = (status: 'connecting' | 'connected' | 'error' | 'closed', msg?: string) => void;

/**
 * 构建讯飞 RTASR 大模型版鉴权 URL
 */
function buildAuthUrl(): string {
  const appId = config.iflytek.appId;
  const accessKeyId = config.iflytek.apiKey; // APIKey = accessKeyId
  const accessKeySecret = config.iflytek.apiSecret; // APISecret = accessKeySecret
  const uuid = crypto.randomUUID();

  // utc 时间格式：2025-09-04T15:38:07+0800
  const now = new Date();
  const offset = -now.getTimezoneOffset();
  const sign = offset >= 0 ? '+' : '-';
  const utc =
    now.getFullYear() +
    '-' +
    String(now.getMonth() + 1).padStart(2, '0') +
    '-' +
    String(now.getDate()).padStart(2, '0') +
    'T' +
    String(now.getHours()).padStart(2, '0') +
    ':' +
    String(now.getMinutes()).padStart(2, '0') +
    ':' +
    String(now.getSeconds()).padStart(2, '0') +
    sign +
    String(Math.abs(offset / 60)).padStart(2, '0') +
    '00';

  // 所有参数（不含 signature），按参数名升序排序
  const params: Record<string, string> = {
    accessKeyId,
    appId,
    audio_encode: 'pcm_s16le',
    lang: 'autodialect',
    samplerate: '16000',
    utc,
    uuid,
  };

  // 说话人分离：role_type=2 开启盲分模式
  if (config.iflytek.roleType === 2) {
    params.role_type = '2';
  }

  // 按 key 升序排序
  const sortedKeys = Object.keys(params).sort();
  // 对每个参数的键和值进行 URL 编码，拼接 baseString
  const encodedPairs: string[] = [];
  for (const key of sortedKeys) {
    const ek = encodeURIComponent(key);
    const ev = encodeURIComponent(params[key]);
    encodedPairs.push(`${ek}=${ev}`);
  }
  const baseString = encodedPairs.join('&');

  // HmacSHA1 加密
  const hmac = crypto.createHmac('sha1', accessKeySecret);
  hmac.update(baseString);
  const signature = hmac.digest('base64');

  // 拼 URL
  const urlParams = new URLSearchParams();
  for (const key of sortedKeys) {
    urlParams.append(key, params[key]);
  }
  urlParams.append('signature', signature);

  return `${config.iflytek.wsUrl}/ast/communicate/v1?${urlParams.toString()}`;
}

export class IflytekAsr {
  private ws: WebSocket | null = null;
  private onAsr: OnAsrCallback;
  private onStatus: OnStatusCallback;
  private sessionStartTime: number;
  private connected = false;
  private sessionId: string | null = null;
  /** 当前说话人 ID（跨帧保持，rl=0 时沿用） */
  private currentSpeaker = 0;
  /** 说话人平滑器（解决长会话漂移） */
  private smoother = new SpeakerSmoother(
    config.iflytek.maxSpeakers,
    config.iflytek.debounceWindowS,
    config.iflytek.minSwitchIntervalS,
  );

  constructor(onAsr: OnAsrCallback, onStatus: OnStatusCallback, sessionStartTime: number) {
    this.onAsr = onAsr;
    this.onStatus = onStatus;
    this.sessionStartTime = sessionStartTime;
  }

  get isConnected(): boolean {
    return this.connected && this.ws?.readyState === WebSocket.OPEN;
  }

  async connect(): Promise<void> {
    const url = buildAuthUrl();
    console.log('[IflytekAsr] 连接讯飞 RTASR:', url.slice(0, 120) + '...');
    this.onStatus('connecting');

    // 解析目标主机
    const targetUrl = new URL(url);

    // 检测代理（Clash 在 127.0.0.1:7890，fake-ip 模式会劫持 DNS）
    const proxyUrl = process.env.https_proxy || process.env.HTTPS_PROXY || '';

    let wsOptions: WebSocket.ClientOptions;

    if (proxyUrl) {
      // 通过 HTTP 代理手动建立 CONNECT 隧道 -> tls.connect
      // 库（https-proxy-agent / socks-proxy-agent）在 Clash fake-ip 下均失败（HPE_INVALID_STATUS）
      const proxy = new URL(proxyUrl);
      const proxyHost = proxy.hostname;
      const proxyPort = parseInt(proxy.port || '7890', 10);

      // 使用讯飞真实 IP 绕过 fake-ip DNS 劫持
      const iflytekRealIp = process.env.IFLYTEK_REAL_IP || targetUrl.hostname;
      const targetPort = parseInt(targetUrl.port || '443', 10);

      wsOptions = await this.buildTunnelThroughProxy(
        proxyHost,
        proxyPort,
        iflytekRealIp,
        targetPort,
        targetUrl.hostname, // SNI 用域名，不用 IP
      );
    } else {
      // 无代理，直连
      wsOptions = {};
    }

    return new Promise<void>((resolve, reject) => {
      let settled = false;
      this.ws = new WebSocket(url, wsOptions);

      const timeout = setTimeout(() => {
        if (!settled) {
          settled = true;
          this.onStatus('error', '连接超时');
          reject(new Error('讯飞 ASR 连接超时'));
          try { this.ws?.close(); } catch { /* ignore */ }
        }
      }, 10000);

      this.ws.on('open', () => {
        clearTimeout(timeout);
        this.connected = true;
        this.currentSpeaker = 0;
        this.smoother.reset();
        console.log('[IflytekAsr] WS 已建立');
        this.onStatus('connected');
        if (!settled) { settled = true; resolve(); }
      });

      this.ws.on('message', (data) => {
        this.handleMessage(data.toString());
      });

      this.ws.on('error', (err: Error) => {
        console.error('[IflytekAsr] WS 错误:', err.message);
        clearTimeout(timeout);
        if (!settled) {
          settled = true;
          this.onStatus('error', err.message);
          reject(err);
        } else {
          this.onStatus('error', err.message);
        }
      });

      this.ws.on('close', (code, reason) => {
        clearTimeout(timeout);
        this.connected = false;
        const r = reason?.toString() || '';
        console.log(`[IflytekAsr] WS 关闭 code=${code} reason=${r}`);
        this.onStatus('closed', `code=${code}`);
        if (!settled) {
          settled = true;
          reject(new Error(`讯飞 ASR 关闭: ${code}`));
        }
      });
    });
  }

  /**
   * 通过 HTTP 代理手动建立 CONNECT 隧道 -> tls.connect
   * 返回 ws 的 agent 或 netStream 选项
   *
   * 流程：http.request CONNECT -> 获取 raw socket -> tls.connect(socket) ->
   *       ws.createConnection 复用该 TLS 连接
   */
  private buildTunnelThroughProxy(
    proxyHost: string,
    proxyPort: number,
    targetHost: string,
    targetPort: number,
    sniHostname: string,
  ): Promise<WebSocket.ClientOptions> {
    return new Promise((resolve, reject) => {
      console.log(
        `[IflytekAsr] 隧道: 代理 ${proxyHost}:${proxyPort} -> 目标 ${targetHost}:${targetPort} (SNI: ${sniHostname})`,
      );

      const req = http.request({
        host: proxyHost,
        port: proxyPort,
        method: 'CONNECT',
        path: `${targetHost}:${targetPort}`,
        headers: {
          Host: `${targetHost}:${targetPort}`,
        },
      });

      req.on('connect', (_res, socket) => {
        if (_res.statusCode !== 200) {
          reject(new Error(`CONNECT 隧道失败: ${_res.statusCode}`));
          return;
        }
        console.log('[IflytekAsr] CONNECT 隧道已建立, 开始 TLS 握手...');

        // 在隧道 socket 上建立 TLS
        const tlsSocket = tls.connect({
          socket,
          servername: sniHostname,
        });

        tlsSocket.on('secureConnect', () => {
          console.log('[IflytekAsr] TLS 握手成功');
          // 让 ws 复用这个已建立的 TLS 连接
          resolve({
            createConnection: () => tlsSocket,
          });
        });

        tlsSocket.on('error', (err) => {
          console.error('[IflytekAsr] TLS 握手失败:', err.message);
          reject(err);
        });
      });

      req.on('error', (err) => {
        console.error('[IflytekAsr] CONNECT 请求失败:', err.message);
        reject(err);
      });

      req.end();
    });
  }

  /**
   * 发送 PCM16 音频帧
   * 讯飞要求 binary frame（直接发二进制，不是 base64）
   */
  sendAudio(base64Pcm: string): void {
    if (!this.ws || this.ws.readyState !== WebSocket.OPEN) return;
    // 将 base64 转为 Buffer 发送
    const buf = Buffer.from(base64Pcm, 'base64');
    this.ws.send(buf);
  }

  /** 发送结束标识 */
  endAudio(): void {
    if (!this.ws || this.ws.readyState !== WebSocket.OPEN) return;
    if (this.sessionId) {
      this.ws.send(JSON.stringify({ end: true, sessionId: this.sessionId }));
    }
  }

  close(): void {
    if (this.ws) {
      try { this.ws.close(); } catch { /* ignore */ }
      this.ws = null;
    }
    this.connected = false;
  }

  // ============================================================

  private handleMessage(raw: string): void {
    let msg: Record<string, any>;
    try {
      msg = JSON.parse(raw);
    } catch {
      console.error('[IflytekAsr] 非 JSON:', raw.slice(0, 100));
      return;
    }

    const msgType = msg.msg_type as string | undefined;
    const data = msg.data as Record<string, any> | undefined;

    // 握手成功
    if (msgType === 'action' && data?.action === 'started') {
      this.sessionId = (data.sessionId as string) || null;
      console.log(`[IflytekAsr] 转写已开始 sid=${this.sessionId}`);
      return;
    }

    // 异常结果（data.normal === false 表示功能异常）
    if (msgType === 'error' || (msgType === 'result' && data?.normal === false)) {
      const code = data?.code as string | undefined;
      const desc = data?.desc as string | undefined;
      console.error(`[IflytekAsr] 错误 code=${code}: ${desc}`);
      this.onStatus('error', `code=${code} ${desc || ''}`);
      return;
    }

    // 转写结果
    if (msgType === 'result') {
      if (!data) return;
      const isFinal = this.checkFinal(data);
      // 按 rl 边界切分成多段，每段单独输出一个 speaker
      const segments = this.extractSegments(data);
      if (segments.length === 0) return;
      const ts = Math.round(((Date.now() - this.sessionStartTime) / 1000) * 10) / 10;
      // 只把最后一段标记为 final（句末），中间段一律非 final
      segments.forEach((seg, i) => {
        this.onAsr({
          text: seg.text,
          isFinal: isFinal && i === segments.length - 1,
          ts,
          speaker: seg.speaker,
        });
      });
      return;
    }

    // 其他消息类型忽略
  }

  /**
   * 从讯飞结果中按说话人边界切分出多段文本
   *
   * 讯飞 role_type=2 盲分模式：rl 字段在**词级别**（cw.rl）返回。
   *   - rl=1/2/3... 表示该词切换到第 N 个说话人
   *   - rl=0 或缺失 表示继续上一说话人
   *   - rl 是字符串类型，需 parseInt
   *
   * 一条讯飞消息可能包含多个说话人交替的词，不能整条只输出一个 speaker。
   * 这里按 rl 变化点切分，每段文本配一个独立 speaker，避免把不同人的话合并。
   * 跨消息保持 currentSpeaker，遇到 rl>0 时更新。Smoother 对每段独立平滑。
   */
  private extractSegments(data: Record<string, unknown>): Array<{ text: string; speaker: number }> {
    try {
      const cn = (data as { cn?: { st?: { rt?: Array<{ ws?: Array<{ cw?: Array<{ w?: string; wp?: string; rl?: number | string }> }> }> } } }).cn;
      if (!cn?.st?.rt) return [];

      const ts = Math.round(((Date.now() - this.sessionStartTime) / 1000) * 10) / 10;

      // 先把词序列展平，每个词记录它所属的说话人（rl>0 切换，rl=0/缺失 沿用）
      interface Word { w: string; speaker: number; }
      const words: Word[] = [];
      for (const rt of cn.st.rt) {
        if (!rt.ws) continue;
        for (const ws of rt.ws) {
          if (!ws.cw) continue;
          for (const cw of ws.cw) {
            if (cw.rl !== undefined && cw.rl !== null) {
              const rlNum = typeof cw.rl === 'number' ? cw.rl : parseInt(String(cw.rl), 10);
              if (!isNaN(rlNum) && rlNum > 0) {
                this.currentSpeaker = rlNum;
              }
            }
            if (cw.w) words.push({ w: cw.w, speaker: this.currentSpeaker });
          }
        }
      }

      if (words.length === 0) return [];

      // 按说话人变化点切分：连续相同 speaker 的词合并为一段
      const segments: Array<{ text: string; speaker: number }> = [];
      let buf = '';
      let bufSpeaker = words[0].speaker;
      for (const word of words) {
        if (word.speaker !== bufSpeaker && buf) {
          segments.push({
            text: buf,
            speaker: this.smoother.smooth(bufSpeaker, ts, buf),
          });
          buf = '';
          bufSpeaker = word.speaker;
        }
        buf += word.w;
      }
      if (buf) {
        segments.push({
          text: buf,
          speaker: this.smoother.smooth(bufSpeaker, ts, buf),
        });
      }
      return segments;
    } catch {
      return [];
    }
  }

  /** 判断是否为最终结果（句末） */
  private checkFinal(data: Record<string, unknown>): boolean {
    try {
      const st = (data as { cn?: { st?: { type?: string } } }).cn?.st;
      return st?.type === '0';
    } catch {
      return false;
    }
  }
}
