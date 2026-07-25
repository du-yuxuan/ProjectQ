// 校准脚本：用真实演讲音频走讯飞 ASR，收集转写结果并分析评分指标
// 用法：npx tsx scripts/calibrate-from-audio.ts <wav文件路径>

import { readFileSync } from 'fs';
import { IflytekAsr, type IflytekAsrResult } from '../src/services/iflytek-asr.js';

// === 填充词 & 逻辑连接词（与 score-engine.ts 完全一致） ===
const FILLER_WORDS = [
  '嗯', '啊', '呃', '那个', '这个', '就是', '然后', '反正', '其实', '就是说', '基本上',
];
const LOGIC_CONNECTORS = [
  '首先', '其次', '然后', '最后', '因此', '所以', '因为', '由于', '不过', '但是',
  '然而', '另外', '此外', '总之', '综上', '也就是说', '换句话说', '核心是', '关键是',
];
const CHINESE_CHAR_RE = /[\u4e00-\u9fff]/g;

function countOccurrences(text: string, words: string[]): number {
  let count = 0;
  for (const w of words) {
    let idx = 0;
    while ((idx = text.indexOf(w, idx)) !== -1) {
      count++;
      idx += w.length;
    }
  }
  return count;
}

function countChineseChars(text: string): number {
  const m = text.match(CHINESE_CHAR_RE);
  return m ? m.length : 0;
}

// === 主逻辑 ===
const wavPath = process.argv[2] || '/tmp/asr-calib/clip1.wav';
const label = process.argv[3] || wavPath.split('/').pop()!;

console.log(`\n========== ${label} ==========`);
console.log(`文件: ${wavPath}`);

// 读取 WAV 文件，跳过 44 字节头，提取 PCM 数据
const wavBuf = readFileSync(wavPath);
const pcmData = wavBuf.subarray(44); // 跳过 WAV header
console.log(`PCM 数据: ${pcmData.length} 字节 (${(pcmData.length / 32000).toFixed(1)}秒 @16kHz 16bit mono)`);

const sessionStartTime = Date.now();
const results: IflytekAsrResult[] = [];

const asr = new IflytekAsr(
  (result) => {
    results.push(result);
    process.stdout.write(result.text);
    if (result.isFinal) process.stdout.write(' | ');
  },
  (status, msg) => {
    console.log(`\n[ASR 状态] ${status} ${msg || ''}`);
  },
  sessionStartTime,
);

console.log('连接讯飞 ASR...');
await asr.connect();
console.log('已连接，开始发送音频...\n');

// 每 40ms 发 1280 字节（讯飞要求）
const FRAME_SIZE = 1280;
const FRAME_INTERVAL = 40; // ms
let offset = 0;
let frameCount = 0;

await new Promise<void>((resolve) => {
  const sendInterval = setInterval(() => {
    if (offset >= pcmData.length) {
      clearInterval(sendInterval);
      console.log('\n\n音频发送完毕，等待最后转写...');
      // 发送结束标识
      asr.endAudio();
      // 等待讯飞返回剩余结果
      setTimeout(() => {
        asr.close();
        resolve();
      }, 3000);
      return;
    }

    const chunk = pcmData.subarray(offset, offset + FRAME_SIZE);
    asr.sendAudio(chunk.toString('base64'));
    offset += FRAME_SIZE;
    frameCount++;
  }, FRAME_INTERVAL);
});

console.log(`\n发送了 ${frameCount} 帧 (${(frameCount * 0.04).toFixed(1)}秒音频)`);
console.log(`收到 ${results.length} 条转写结果\n`);

// === 分析 ===
console.log('========== 分析结果 ==========\n');

// 合并所有 final 文本
const finalTexts = results.filter(r => r.isFinal).map(r => r.text);
const allText = results.map(r => r.text).join('');
const finalText = finalTexts.join('');
const charCount = countChineseChars(allText);

console.log(`【文本统计】`);
console.log(`  总转写文本: "${allText.slice(0, 100)}${allText.length > 100 ? '...' : ''}"`);
console.log(`  中文字符数: ${charCount}`);
console.log(`  final 段数: ${finalTexts.length}`);
console.log();

// 填充词分析
const fillerCount = countOccurrences(allText, FILLER_WORDS);
const fillerDensity = charCount > 0 ? (fillerCount / charCount * 100).toFixed(2) : '0';
console.log(`【填充词分析】`);
console.log(`  填充词总数: ${fillerCount}`);
console.log(`  填充词密度: ${fillerDensity}% (每百字 ${fillerCount > 0 ? (fillerCount / (charCount / 100)).toFixed(2) : '0'} 个)`);
if (fillerCount > 0) {
  const details: Record<string, number> = {};
  for (const w of FILLER_WORDS) {
    let c = 0, idx = 0;
    while ((idx = allText.indexOf(w, idx)) !== -1) { c++; idx += w.length; }
    if (c > 0) details[w] = c;
  }
  console.log(`  明细: ${JSON.stringify(details)}`);
}
console.log();

// 逻辑连接词分析
const connectorCount = countOccurrences(allText, LOGIC_CONNECTORS);
const connectorDensity = charCount > 0 ? (connectorCount / charCount * 100).toFixed(2) : '0';
console.log(`【逻辑连接词分析】`);
console.log(`  连接词总数: ${connectorCount}`);
console.log(`  连接词密度: ${connectorDensity}% (每百字 ${connectorCount > 0 ? (connectorCount / (charCount / 100)).toFixed(2) : '0'} 个)`);
if (connectorCount > 0) {
  const details: Record<string, number> = {};
  for (const w of LOGIC_CONNECTORS) {
    let c = 0, idx = 0;
    while ((idx = allText.indexOf(w, idx)) !== -1) { c++; idx += w.length; }
    if (c > 0) details[w] = c;
  }
  console.log(`  明细: ${JSON.stringify(details)}`);
}
console.log();

// 语速分析
const audioDuration = frameCount * 0.04; // 秒
const firstTs = results.length > 0 ? results[0].ts : 0;
const lastTs = results.length > 0 ? results[results.length - 1].ts : 0;
const speechDuration = lastTs - firstTs;

console.log(`【语速分析】`);
console.log(`  音频时长: ${audioDuration.toFixed(1)}秒`);
console.log(`  转写时间跨度: ${firstTs.toFixed(1)}s -> ${lastTs.toFixed(1)}s (${speechDuration.toFixed(1)}秒)`);
if (speechDuration > 0) {
  const charsPerSec = charCount / speechDuration;
  console.log(`  净语速: ${charsPerSec.toFixed(2)} 字/秒 (含填充词)`);
  const validChars = Math.max(0, charCount - fillerCount);
  const netCharsPerSec = validChars / speechDuration;
  console.log(`  有效语速: ${netCharsPerSec.toFixed(2)} 字/秒 (扣除填充词)`);
  console.log(`  → 当前 paceToScore 映射: ${paceToScore(netCharsPerSec)} 分`);
}
console.log();

// 段级分析
console.log(`【段级分析】`);
for (let i = 0; i < finalTexts.length; i++) {
  const segText = finalTexts[i];
  const segChars = countChineseChars(segText);
  const segFillers = countOccurrences(segText, FILLER_WORDS);
  const segConnectors = countOccurrences(segText, LOGIC_CONNECTORS);
  const segStart = results.filter(r => r.isFinal)[i]?.ts ?? 0;
  const segEnd = results.filter(r => r.isFinal)[i + 1]?.ts ?? lastTs;
  const segDur = segEnd - segStart;
  const segCps = segDur > 0 ? (segChars / segDur).toFixed(2) : 'N/A';
  console.log(`  段${i + 1}: [${segStart.toFixed(1)}-${segEnd.toFixed(1)}s] ${segChars}字 ${segDur.toFixed(1)}s ${segCps}字/秒 填充${segFillers} 连接${segConnectors}`);
  console.log(`    "${segText.slice(0, 80)}${segText.length > 80 ? '...' : ''}"`);
}
console.log();

// 停顿分析（基于 ASR 时间戳间隔）
console.log(`【停顿分析】`);
const tsList = results.map(r => r.ts);
let pauses = 0;
let maxPause = 0;
const pauseThreshold = 1.5; // 秒
for (let i = 1; i < tsList.length; i++) {
  const gap = tsList[i] - tsList[i - 1];
  if (gap >= pauseThreshold) {
    pauses++;
    maxPause = Math.max(maxPause, gap);
    console.log(`  停顿 ${gap.toFixed(1)}秒 @ ${tsList[i - 1].toFixed(1)}s`);
  }
}
console.log(`  停顿次数(>${pauseThreshold}s): ${pauses}`);
console.log(`  最长停顿: ${maxPause.toFixed(1)}秒`);
const pauseRate = speechDuration > 0 ? (pauses * pauseThreshold / speechDuration).toFixed(2) : '0';
console.log(`  估算停顿率: ${pauseRate}`);
console.log();

// === 当前评分引擎模拟 ===
console.log(`【当前评分引擎模拟（如对此段文本评分）】`);
const fillerDensityVal = charCount > 0 ? fillerCount / charCount : 0;
const fillerScore = Math.max(0, 10 - fillerDensityVal * 200);
const pauseScore = Math.max(0, 10 - parseFloat(pauseRate) * 100);
const fluency = Math.round(Math.min(10, Math.max(0, fillerScore * 0.6 + pauseScore * 0.4)));
const connectorDensityVal = charCount > 0 ? connectorCount / charCount : 0;
const connectorScore = Math.min(10, connectorDensityVal * 250);
const completenessScore = Math.min(10, charCount / 5);
const fillerPenalty = Math.max(0, 10 - fillerDensityVal * 150);
const logic = Math.round(Math.min(10, Math.max(0, connectorScore * 0.4 + completenessScore * 0.3 + fillerPenalty * 0.3)));
const validChars = Math.max(0, charCount - fillerCount);
const netCps = speechDuration > 0 ? validChars / speechDuration : 0;
const rawPace = paceToScore(netCps);
const pace = Math.round((rawPace * 0.6 + (1 - parseFloat(pauseRate)) * 10 * 0.4) * 10) / 10;
console.log(`  fluency = ${fluency} (填充词分=${fillerScore.toFixed(1)}, 停顿分=${pauseScore.toFixed(1)})`);
console.log(`  logic   = ${logic} (连接词分=${connectorScore.toFixed(1)}, 完整性=${completenessScore.toFixed(1)}, 填充惩罚=${fillerPenalty.toFixed(1)})`);
console.log(`  pace    = ${pace} (净语速=${netCps.toFixed(2)}字/秒 -> 原始分=${rawPace.toFixed(1)})`);
console.log(`  overall = ${((fluency + logic + pace) / 3).toFixed(1)}`);
console.log();

process.exit(0);

// === 复制 paceToScore 逻辑用于分析 ===
function paceToScore(charsPerSec: number): number {
  if (charsPerSec < 1.5) {
    return (charsPerSec / 1.5) * 2;
  } else if (charsPerSec < 2.5) {
    return 3 + ((charsPerSec - 1.5) / 1.0) * 2;
  } else if (charsPerSec < 3.3) {
    return 6 + ((charsPerSec - 2.5) / 0.8) * 1;
  } else if (charsPerSec < 4.7) {
    return 8 + ((charsPerSec - 3.3) / 1.4) * 1;
  } else if (charsPerSec < 5.5) {
    return 7 - ((charsPerSec - 4.7) / 0.8) * 1;
  } else if (charsPerSec < 6.5) {
    return 5 - ((charsPerSec - 5.5) / 1.0) * 2;
  } else {
    return Math.max(0, 2 - ((charsPerSec - 6.5) / 1.0) * 2);
  }
}
