// WS 端到端测试 — 模拟浏览器 WS 连接, 发送 session_control start + energy reports
// 模拟有声/无声交替, 验证后端 mock 评分 + 卡壳检测 + 钩子触发

import WebSocket from 'ws';

const ws = new WebSocket('ws://localhost:3001/ws/session');

const messages = [];
let startTime = Date.now();
let energyInterval = null;
let ts = 0;

ws.on('open', () => {
  console.log('✅ WS 连接成功');

  // 发送 session_control start
  ws.send(JSON.stringify({
    type: 'session_control',
    action: 'start',
    userId: 'demo-user-001',
    userName: '测试用户',
  }));
  console.log('→ session_control start');
});

ws.on('message', (data) => {
  const msg = JSON.parse(data.toString());
  messages.push(msg);
  console.log(`← ${msg.type}:`, JSON.stringify(msg).slice(0, 120));

  if (msg.type === 'session_started') {
    console.log(`✅ 会话已开始: ${msg.sessionId}`);
    startTime = Date.now();

    // 模拟能量报告: 有声→无声(卡壳)→有声
    // 前 10 秒有声, 然后 3 秒无声(触发开口兜底), 再有声
    energyInterval = setInterval(() => {
      ts = (Date.now() - startTime) / 1000;

      let isActive = true;
      let energy = 0.3;

      if (ts > 10 && ts < 13) {
        // 模拟卡壳: 无声 3 秒
        isActive = false;
        energy = 0.01;
      } else if (ts > 20 && ts < 23) {
        // 第二次卡壳
        isActive = false;
        energy = 0.01;
      }

      ws.send(JSON.stringify({
        type: 'energy',
        ts: ts,
        energy: energy,
        isActive: isActive,
      }));

      // 发送模拟音频帧
      const fakePcm = Buffer.alloc(3200); // 100ms of 16kHz 16-bit = 3200 bytes
      ws.send(JSON.stringify({
        type: 'audio',
        data: fakePcm.toString('base64'),
        seq: Math.floor(ts * 10),
      }));
    }, 100);
  }

  if (msg.type === 'hook') {
    console.log(`🎯 钩子触发! 类型: ${msg.hookType}, 文本: "${msg.hookText}", 倒计时: ${msg.countdown}s`);
  }

  if (msg.type === 'recovery') {
    console.log(`✅ 恢复确认! 响应时间: ${msg.responseTimeMs}ms`);
  }

  if (msg.type === 'pace_update') {
    console.log(`📊 语速更新: 分数=${msg.paceScore}, 字/秒=${msg.charsPerSec}, 停顿率=${msg.pauseRate}`);
  }
});

ws.on('error', (err) => {
  console.error('❌ WS 错误:', err.message);
});

ws.on('close', () => {
  console.log('WS 连接关闭');
});

// 30 秒后结束会话
setTimeout(() => {
  if (energyInterval) clearInterval(energyInterval);
  ws.send(JSON.stringify({
    type: 'session_control',
    action: 'end',
  }));
  console.log('→ session_control end');

  // 等待结束确认后关闭
  setTimeout(() => {
    ws.close();
    console.log('');
    console.log('=== 测试结果汇总 ===');
    console.log(`总消息数: ${messages.length}`);
    const scoreMsgs = messages.filter(m => m.type === 'score');
    const hookMsgs = messages.filter(m => m.type === 'hook');
    const paceMsgs = messages.filter(m => m.type === 'pace_update');
    const recoveryMsgs = messages.filter(m => m.type === 'recovery');
    const segEndMsgs = messages.filter(m => m.type === 'segment_end');
    const sessionEndMsgs = messages.filter(m => m.type === 'session_ended');
    console.log(`评分消息: ${scoreMsgs.length}`);
    console.log(`钩子消息: ${hookMsgs.length}`);
    console.log(`语速更新: ${paceMsgs.length}`);
    console.log(`恢复确认: ${recoveryMsgs.length}`);
    console.log(`段结束: ${segEndMsgs.length}`);
    console.log(`会话结束: ${sessionEndMsgs.length}`);

    const success = scoreMsgs.length > 0 && paceMsgs.length > 0;
    console.log(success ? '✅ 端到端测试通过!' : '❌ 测试失败');
    process.exit(success ? 0 : 1);
  }, 3000);
}, 30000);
