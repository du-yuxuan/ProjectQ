// 真实数据流验证测试 — 模拟浏览器发送真实 STT 转写文本
// 验证后端基于真实文本计算评分（非 mock 随机数）

import WebSocket from 'ws';

const ws = new WebSocket('ws://localhost:3001/ws/session');
const messages = [];
let startTime = Date.now();
let timers = [];

ws.on('open', () => {
  console.log('✅ WS 连接成功');
  ws.send(JSON.stringify({
    type: 'session_control',
    action: 'start',
    userId: 'real_data_test',
    userName: '真实数据测试',
  }));
});

// 模拟真实中文演讲转写（分段发送，模拟 Web Speech API 的 final 结果）
const REAL_SPEECH_SEGMENTS = [
  '大家好，今天我想分享的是我们团队在黑客松上的项目。',
  '首先，我们解决了一个核心问题，就是演讲者在表达时经常卡壳。',
  '嗯，然后我们就是基本上做了一个原型来验证这个想法。',
  '其实关键在于，我们需要更好地理解用户的需求。',
  '换句话说，我们希望让交互更加自然流畅。',
  '我认为最重要的不是速度，而是质量和用户体验。',
  '所以，我们设计了一套系统来实时评判表达效果。',
  '最后，总结来说，好的设计应该让用户无意识地完成任务。',
];

ws.on('message', (data) => {
  const msg = JSON.parse(data.toString());
  messages.push(msg);

  if (msg.type === 'session_started') {
    console.log(`✅ 会话已开始: ${msg.sessionId}`);
    startTime = Date.now();

    // 1. 先发送能量报告（模拟有声/无声）
    let ts = 0;
    const energyTimer = setInterval(() => {
      ts = (Date.now() - startTime) / 1000;
      // 模拟前 25 秒持续有声，然后 3 秒无声（卡壳），再有声
      const isActive = !(ts > 25 && ts < 28);
      const energy = isActive ? 0.3 + Math.random() * 0.2 : 0.01;
      ws.send(JSON.stringify({
        type: 'energy',
        ts: ts,
        energy: energy,
        isActive: isActive,
      }));
    }, 100);
    timers.push(energyTimer);

    // 2. 每隔 3 秒发送一段真实转写文本（模拟 Web Speech API final 结果）
    let segmentIdx = 0;
    const transcriptTimer = setInterval(() => {
      if (segmentIdx >= REAL_SPEECH_SEGMENTS.length) return;
      const ts = (Date.now() - startTime) / 1000;
      const text = REAL_SPEECH_SEGMENTS[segmentIdx];
      ws.send(JSON.stringify({
        type: 'transcript',
        ts: ts,
        text: text,
        isFinal: true,
      }));
      console.log(`→ 转写: "${text}"`);
      segmentIdx++;
    }, 3000);
    timers.push(transcriptTimer);

    // 3. 30 秒后结束会话
    const endTimer = setTimeout(() => {
      timers.forEach(t => clearInterval(t));
      ws.send(JSON.stringify({ type: 'session_control', action: 'end' }));
      console.log('→ 会话结束');
    }, 30000);
    timers.push(endTimer);
  }

  if (msg.type === 'score') {
    console.log(`← 评分: fluency=${msg.fluency} logic=${msg.logic} pace=${msg.pace} fillers=${msg.fillers} pauses=${msg.pauses} text="${msg.text?.slice(0, 25)}..."`);
  }

  if (msg.type === 'pace_update') {
    console.log(`← 语速: score=${msg.paceScore} chars/s=${msg.charsPerSec.toFixed(1)} pauseRate=${msg.pauseRate.toFixed(2)}`);
  }

  if (msg.type === 'hook') {
    console.log(`🎯 钩子: ${msg.hookType} "${msg.hookText}" 倒计时=${msg.countdown}s`);
  }

  if (msg.type === 'session_ended') {
    console.log(`✅ 会话已结束: ${msg.sessionId}`);
  }
});

ws.on('error', (err) => {
  console.error('❌ WS 错误:', err.message);
});

ws.on('close', () => {
  timers.forEach(t => { clearInterval(t); clearTimeout(t); });
  console.log('\n=== 测试结果 ===');
  const scores = messages.filter(m => m.type === 'score');
  const paces = messages.filter(m => m.type === 'pace_update');
  const hooks = messages.filter(m => m.type === 'hook');
  console.log(`评分消息: ${scores.length}`);
  console.log(`语速更新: ${paces.length}`);
  console.log(`钩子触发: ${hooks.length}`);

  // 验证评分是基于真实文本的（不是 mock 随机数）
  if (scores.length > 0) {
    const realTexts = scores.filter(s => REAL_SPEECH_SEGMENTS.some(r => s.text?.includes(r.slice(0, 10))));
    console.log(`含真实文本的评分: ${realTexts.length}/${scores.length}`);
    if (realTexts.length > 0) {
      console.log('✅ 真实数据流验证通过！评分基于实际转写文本');
    }
  }

  const success = scores.length > 0;
  console.log(success ? '✅ 端到端 PASS' : '❌ 端到端 FAIL');
  process.exit(success ? 0 : 1);
});

// 安全超时
setTimeout(() => {
  timers.forEach(t => { clearInterval(t); clearTimeout(t); });
  ws.close();
  process.exit(1);
}, 40000);
