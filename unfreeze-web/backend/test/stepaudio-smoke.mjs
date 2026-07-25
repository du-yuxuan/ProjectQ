// StepAudio 连接 + 会话冒烟测试
import WebSocket from 'ws';

const SAMPLE_RATE = 24000;
const ws = new WebSocket('ws://localhost:3001/ws/session');
const msgs = [];
let audioTimer = null;
let done = false;

function finish(code) {
  if (done) return;
  done = true;
  if (audioTimer) clearInterval(audioTimer);
  try { ws.close(); } catch {}
  const types = msgs.map((m) => m.type);
  console.log('\n=== 收到消息类型 ===');
  console.log([...new Set(types)].join(', '));
  console.log('asr_transcript:', msgs.filter((m) => m.type === 'asr_transcript').length);
  console.log('score:', msgs.filter((m) => m.type === 'score').length);
  console.log('hook:', msgs.filter((m) => m.type === 'hook').length);
  console.log('relay_status:', msgs.filter((m) => m.type === 'relay_status').map((m) => m.status + (m.message ? ':' + m.message : '')).join(' | '));
  console.log('errors:', msgs.filter((m) => m.type === 'error').map((m) => m.message).join(' | '));
  process.exit(code);
}

ws.on('open', () => {
  console.log('WS open');
  ws.send(JSON.stringify({
    type: 'session_control',
    action: 'start',
    userId: 'stepaudio_smoke',
    userName: 'StepAudio冒烟',
  }));
});

ws.on('message', (data) => {
  const msg = JSON.parse(data.toString());
  msgs.push(msg);
  console.log('←', msg.type, msg.status || msg.hookType || (msg.text ? `"${String(msg.text).slice(0, 30)}"` : ''), msg.message || '');

  if (msg.type === 'session_started') {
    // 发送约 3 秒的静音 + 正弦波模拟语音能量（24kHz PCM16）
    let t = 0;
    const frameSamples = 2400; // 100ms @ 24k
    audioTimer = setInterval(() => {
      t += 0.1;
      const pcm = new Int16Array(frameSamples);
      // 前 0.5s 静音，之后 440Hz 正弦（模拟有声）
      if (t > 0.5 && t < 4.5) {
        for (let i = 0; i < frameSamples; i++) {
          const sample = Math.sin(2 * Math.PI * 220 * (t + i / SAMPLE_RATE)) * 0.25;
          pcm[i] = (sample * 0x7fff) | 0;
        }
      }
      const b64 = Buffer.from(pcm.buffer).toString('base64');
      ws.send(JSON.stringify({ type: 'audio', data: b64, seq: Math.floor(t * 10) }));
      ws.send(JSON.stringify({
        type: 'energy',
        ts: t,
        energy: t > 0.5 && t < 4.5 ? 0.4 : 0.01,
        isActive: t > 0.5 && t < 4.5,
      }));
      if (t >= 8) {
        clearInterval(audioTimer);
        audioTimer = null;
        ws.send(JSON.stringify({ type: 'session_control', action: 'end' }));
      }
    }, 100);
  }

  if (msg.type === 'session_ended') {
    setTimeout(() => finish(0), 500);
  }
});

ws.on('error', (e) => {
  console.error('WS error', e.message);
  finish(1);
});

ws.on('close', () => {
  if (!done) {
    console.log('WS closed early');
    finish(msgs.some((m) => m.type === 'session_started') ? 0 : 1);
  }
});

setTimeout(() => {
  console.log('timeout 20s');
  try { ws.send(JSON.stringify({ type: 'session_control', action: 'end' })); } catch {}
  setTimeout(() => finish(1), 1000);
}, 20000);
