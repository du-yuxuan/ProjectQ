// 讯飞 ASR 离线测试脚本
// 读取 PCM 文件，按 40ms 帧（1280 字节）发送到讯飞 RTASR 大模型
// 记录所有返回的原始 JSON，用于分析说话人分离稳定性和文本质量

import crypto from 'crypto';
import http from 'http';
import tls from 'tls';
import fs from 'fs';
import WebSocket from 'ws';
import dotenv from 'dotenv';

dotenv.config();

const APP_ID = process.env.IFLYTEK_APPID!;
const ACCESS_KEY_ID = process.env.IFLYTEK_API_KEY!;
const ACCESS_KEY_SECRET = process.env.IFLYTEK_API_SECRET!;
const WS_URL = process.env.IFLYTEK_WS_URL!;
const REAL_IP = process.env.IFLYTEK_REAL_IP;

const PCM_FILE = '/Volumes/Workbench/Competitions/AdvX26/test_segment_60s.wav';

// 跳过 WAV 头（44 字节）
const PCM_DATA = fs.readFileSync(PCM_FILE).subarray(44);
console.log(`PCM 数据: ${PCM_DATA.length} 字节, ${PCM_DATA.length / 32000} 秒`);

// 鉴权 URL
function buildAuthUrl(): string {
  const uuid = crypto.randomUUID();
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

  const params: Record<string, string> = {
    accessKeyId: ACCESS_KEY_ID,
    appId: APP_ID,
    audio_encode: 'pcm_s16le',
    lang: 'autodialect',
    samplerate: '16000',
    utc,
    uuid,
    role_type: '2',
  };

  const sortedKeys = Object.keys(params).sort();
  const encodedPairs: string[] = [];
  for (const key of sortedKeys) {
    const ek = encodeURIComponent(key);
    const ev = encodeURIComponent(params[key]);
    encodedPairs.push(`${ek}=${ev}`);
  }
  const baseString = encodedPairs.join('&');
  const hmac = crypto.createHmac('sha1', ACCESS_KEY_SECRET);
  hmac.update(baseString);
  const signature = hmac.digest('base64');

  const urlParams = new URLSearchParams();
  for (const key of sortedKeys) {
    urlParams.append(key, params[key]);
  }
  urlParams.append('signature', signature);
  return `${WS_URL}/ast/communicate/v1?${urlParams.toString()}`;
}

async function main() {
  const url = buildAuthUrl();
  const targetUrl = new URL(url);

  // 代理检测
  const proxyUrl = process.env.https_proxy || process.env.HTTPS_PROXY || '';

  let wsOptions: WebSocket.ClientOptions = {};

  if (proxyUrl) {
    console.log(`检测到代理: ${proxyUrl}, 建立隧道...`);
    const proxy = new URL(proxyUrl);
    const proxyHost = proxy.hostname;
    const proxyPort = parseInt(proxy.port || '7890', 10);
    const targetHost = REAL_IP || targetUrl.hostname;
    const targetPort = parseInt(targetUrl.port || '443', 10);

    wsOptions = await new Promise((resolve, reject) => {
      const req = http.request({
        host: proxyHost,
        port: proxyPort,
        method: 'CONNECT',
        path: `${targetHost}:${targetPort}`,
        headers: { Host: `${targetHost}:${targetPort}` },
      });

      req.on('connect', (_res, socket) => {
        if (_res.statusCode !== 200) {
          reject(new Error(`CONNECT 隧道失败: ${_res.statusCode}`));
          return;
        }
        const tlsSocket = tls.connect({ socket, servername: targetUrl.hostname });
        tlsSocket.on('secureConnect', () => {
          resolve({ createConnection: () => tlsSocket });
        });
        tlsSocket.on('error', reject);
      });

      req.on('error', reject);
      req.end();
    });
  }

  const allResults: any[] = [];
  let allText = '';

  return new Promise<void>((resolve) => {
    const ws = new WebSocket(url, wsOptions);
    let sessionId: string | null = null;
    let startTime = Date.now();

    const timeout = setTimeout(() => {
      console.log('超时，关闭连接');
      ws.close();
    }, 30000);

    ws.on('open', () => {
      console.log('WS 已建立，开始发送音频...');
      startTime = Date.now();

      // 按 40ms 帧（1280 字节）发送 PCM
      const frameSize = 1280;
      let offset = 0;
      let frameNum = 0;

      const sendFrame = () => {
        if (offset >= PCM_DATA.length) {
          // 发送结束标识
          if (sessionId) {
            ws.send(JSON.stringify({ end: true, sessionId }));
          }
          console.log('音频发送完毕，等待最终结果...');
          return;
        }

        const chunk = PCM_DATA.subarray(offset, offset + frameSize);
        ws.send(chunk);
        offset += frameSize;
        frameNum++;

        // 40ms 间隔
        setTimeout(sendFrame, 40);
      };

      sendFrame();
    });

    ws.on('message', (data) => {
      const raw = data.toString();
      let msg: any;
      try {
        msg = JSON.parse(raw);
      } catch {
        return;
      }

      const msgType = msg.msg_type as string | undefined;
      const msgData = msg.data;

      if (msgType === 'action' && msgData?.action === 'started') {
        sessionId = msgData.sessionId;
        console.log(`转写已开始 sid=${sessionId}`);
        return;
      }

      if (msgType === 'error' || (msgType === 'result' && msgData?.normal === false)) {
        console.error(`错误: code=${msgData?.code} desc=${msgData?.desc}`);
        return;
      }

      if (msgType === 'result' && msgData) {
        // 记录完整原始数据
        const ts = Math.round(((Date.now() - startTime) / 1000) * 10) / 10;
        const entry = { ts, raw: msgData };
        allResults.push(entry);

        // 提取文本和说话人信息
        const cn = msgData.cn;
        if (cn?.st?.rt) {
          let text = '';
          let rlValues: number[] = [];
          for (const rt of cn.st.rt) {
            if (!rt.ws) continue;
            for (const ws of rt.ws) {
              if (!ws.cw) continue;
              for (const cw of ws.cw) {
                if (cw.w) text += cw.w;
                if (cw.rl !== undefined && cw.rl !== null) {
                  const rlNum = typeof cw.rl === 'number' ? cw.rl : parseInt(String(cw.rl), 10);
                  if (!isNaN(rlNum)) rlValues.push(rlNum);
                }
              }
            }
          }
          if (text) {
            allText += text;
            const type = cn.st.type;
            const isFinal = type === '0';
            console.log(`[ts=${ts}s] type=${type} final=${isFinal} rl=[${rlValues.join(',')}] text="${text}"`);
          }
        }
        return;
      }

      if (msgType === 'result' && msgData?.action === 'ended') {
        console.log('转写结束');
        clearTimeout(timeout);

        // 输出汇总
        console.log('\n========== 汇总 ==========');
        console.log(`总结果数: ${allResults.length}`);
        console.log(`全文 (${allText.length} 字):`);
        console.log(allText);

        // 分析 rl 值分布
        const rlStats: Record<number, number> = {};
        for (const r of allResults) {
          const cn = r.raw?.cn;
          if (!cn?.st?.rt) continue;
          for (const rt of cn.st.rt) {
            if (!rt.ws) continue;
            for (const ws of rt.ws) {
              if (!ws.cw) continue;
              for (const cw of ws.cw) {
                if (cw.rl !== undefined && cw.rl !== null) {
                  const rlNum = typeof cw.rl === 'number' ? cw.rl : parseInt(String(cw.rl), 10);
                  if (!isNaN(rlNum)) {
                    rlStats[rlNum] = (rlStats[rlNum] || 0) + 1;
                  }
                }
              }
            }
          }
        }
        console.log('\nrl 值分布 (字词级):');
        for (const [rl, count] of Object.entries(rlStats).sort((a, b) => Number(a[0]) - Number(b[0]))) {
          console.log(`  rl=${rl}: ${count} 次`);
        }

        // 统计填充词
        const FILLERS = ['嗯', '啊', '呃', '那个', '这个', '就是', '然后', '反正', '其实', '就是说', '基本上'];
        let fillerCount = 0;
        const fillerHits: Record<string, number> = {};
        for (const f of FILLERS) {
          let idx = 0;
          let cnt = 0;
          while ((idx = allText.indexOf(f, idx)) !== -1) {
            cnt++;
            idx += f.length;
          }
          if (cnt > 0) {
            fillerHits[f] = cnt;
            fillerCount += cnt;
          }
        }
        console.log(`\n填充词统计 (${fillerCount} 个):`);
        for (const [word, count] of Object.entries(fillerHits).sort((a, b) => b[1] - a[1])) {
          console.log(`  "${word}": ${count} 次`);
        }

        // 中文字符数
        const chineseChars = allText.match(/[\u4e00-\u9fff]/g);
        const charCount = chineseChars ? chineseChars.length : 0;
        const audioDuration = 60; // 60 秒
        const charsPerSec = charCount / audioDuration;
        const fillerDensity = charCount > 0 ? fillerCount / charCount : 0;

        console.log(`\n文本特征:`);
        console.log(`  中文字符数: ${charCount}`);
        console.log(`  音频时长: ${audioDuration}s`);
        console.log(`  字/秒: ${charsPerSec.toFixed(2)}`);
        console.log(`  填充词密度: ${fillerDensity.toFixed(4)} (${(fillerDensity * 100).toFixed(2)}%)`);
        console.log(`  填充词/秒: ${(fillerCount / audioDuration).toFixed(2)}`);

        // 保存原始数据
        fs.writeFileSync(
          '/Volumes/Workbench/Competitions/AdvX26/asr_test_results.json',
          JSON.stringify({ allResults, allText, rlStats, fillerHits, charCount, charsPerSec, fillerDensity }, null, 2),
        );
        console.log('\n原始数据已保存到 asr_test_results.json');

        ws.close();
        resolve();
      }
    });

    ws.on('error', (err: Error) => {
      console.error('WS 错误:', err.message);
      clearTimeout(timeout);
      resolve();
    });

    ws.on('close', () => {
      console.log('WS 关闭');
      clearTimeout(timeout);
      resolve();
    });
  });
}

main().catch(console.error);
