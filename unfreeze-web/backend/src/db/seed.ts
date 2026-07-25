// Unfreeze 种子数据 — 生成示例用户、会话、画像、凭证
// 运行: npx tsx src/db/seed.ts

import { prisma } from './index.js';

async function main() {
  console.log('🌱 开始播种 Unfreeze 示例数据...');

  // --- 用户 ---
  const user = await prisma.user.upsert({
    where: { id: 'demo-user-001' },
    update: {},
    create: {
      id: 'demo-user-001',
      name: '演示用户',
    },
  });
  console.log(`  ✅ 用户: ${user.name} (${user.id})`);

  // --- 会话 1: 3 天前 ---
  const session1Start = new Date(Date.now() - 3 * 24 * 60 * 60 * 1000);
  const session1 = await prisma.session.create({
    data: {
      id: 'demo-session-001',
      userId: user.id,
      startTime: session1Start,
      endTime: new Date(session1Start.getTime() + 120_000),
      duration: 120,
      overallScore: 6,
      transcript:
        '大家好，今天我想分享的是我们团队在 AdventureX 黑客松上的项目。嗯…这个项目叫 Unfreeze，是一个表达指引眼镜系统。那个…主要解决四个痛点。',
      segments: {
        create: [
          {
            ts: 0,
            duration: 30,
            fluencyScore: 5,
            logicScore: 7,
            paceScore: 6,
            fillerCount: 3,
            pauseCount: 2,
            text: '大家好，今天我想分享的是我们团队在 AdventureX 黑客松上的项目。',
          },
          {
            ts: 30,
            duration: 30,
            fluencyScore: 6,
            logicScore: 7,
            paceScore: 5,
            fillerCount: 2,
            pauseCount: 3,
            text: '嗯…这个项目叫 Unfreeze，是一个表达指引眼镜系统。',
          },
          {
            ts: 60,
            duration: 30,
            fluencyScore: 5,
            logicScore: 6,
            paceScore: 6,
            fillerCount: 2,
            pauseCount: 1,
            text: '那个…主要解决四个痛点。',
          },
          {
            ts: 90,
            duration: 30,
            fluencyScore: 6,
            logicScore: 8,
            paceScore: 7,
            fillerCount: 1,
            pauseCount: 1,
            text: '首先是表达效果评判，然后是兜底递钩，能力画像，最后是链上铸证。',
          },
        ],
      },
      hookEvents: {
        create: [
          {
            ts: 35,
            hookType: '开口',
            hookText: '接着说',
            countdown: 3,
            responseTimeMs: 2800,
            recovered: true,
            feedback: 1,
          },
          {
            ts: 62,
            hookType: '思路',
            hookText: '核心是',
            countdown: 3,
            responseTimeMs: 1500,
            recovered: true,
            feedback: 1,
          },
        ],
      },
    },
  });
  console.log(`  ✅ 会话1: ${session1.id} (评分 ${session1.overallScore})`);

  // --- 会话 2: 1 天前 ---
  const session2Start = new Date(Date.now() - 1 * 24 * 60 * 60 * 1000);
  const session2 = await prisma.session.create({
    data: {
      id: 'demo-session-002',
      userId: user.id,
      startTime: session2Start,
      endTime: new Date(session2Start.getTime() + 90_000),
      duration: 90,
      overallScore: 8,
      transcript:
        '各位好，Unfreeze 是一个在真实表达场景里既兜底又成长的系统。核心在于实时评判表达效果，卡壳时递钩救场，跨会话积累能力画像，并铸成链上凭证。',
      segments: {
        create: [
          {
            ts: 0,
            duration: 45,
            fluencyScore: 8,
            logicScore: 9,
            paceScore: 8,
            fillerCount: 0,
            pauseCount: 1,
            text: '各位好，Unfreeze 是一个在真实表达场景里既兜底又成长的系统。',
          },
          {
            ts: 45,
            duration: 45,
            fluencyScore: 8,
            logicScore: 9,
            paceScore: 7,
            fillerCount: 1,
            pauseCount: 1,
            text: '核心在于实时评判表达效果，卡壳时递钩救场，跨会话积累能力画像，并铸成链上凭证。',
          },
        ],
      },
      hookEvents: {
        create: [
          {
            ts: 47,
            hookType: '衔接',
            hookText: '但重点是',
            countdown: 2,
            responseTimeMs: 1200,
            recovered: true,
            feedback: 1,
          },
        ],
      },
    },
  });
  console.log(`  ✅ 会话2: ${session2.id} (评分 ${session2.overallScore})`);

  // --- 画像快照 ---
  const profile = await prisma.profileSnapshot.create({
    data: {
      userId: user.id,
      metrics: JSON.stringify({
        fluencyAvg: 7.0,
        logicAvg: 7.8,
        paceAvg: 6.8,
        sessionsCount: 2,
        totalDuration: 210,
      }),
      weaknesses: JSON.stringify(['流畅度待提升', '停顿控制需加强']),
      strengths: JSON.stringify(['逻辑清晰', '选题聚焦']),
      trendData: JSON.stringify([
        { date: session1Start.toISOString(), fluencyAvg: 5.5, logicAvg: 7.0, paceAvg: 6.0 },
        { date: session2Start.toISOString(), fluencyAvg: 8.0, logicAvg: 9.0, paceAvg: 7.5 },
      ]),
    },
  });
  console.log(`  ✅ 画像快照: ${profile.id}`);

  // --- 凭证 ---
  const cred = await prisma.credential.create({
    data: {
      userId: user.id,
      chainTxHash: 'mock_tx_001_initial',
      milestone: '首次演讲',
      metadata: JSON.stringify({ sessionCount: 1, date: session1Start.toISOString() }),
    },
  });
  console.log(`  ✅ 凭证: ${cred.milestone} (${cred.chainTxHash})`);

  // --- 第二个用户（参观者模拟） ---
  const visitor = await prisma.user.upsert({
    where: { id: 'demo-visitor-001' },
    update: {},
    create: {
      id: 'demo-visitor-001',
      name: '参观者',
    },
  });
  console.log(`  ✅ 用户: ${visitor.name} (${visitor.id})`);

  await prisma.session.create({
    data: {
      userId: visitor.id,
      startTime: new Date(Date.now() - 2 * 60 * 60 * 1000),
      endTime: new Date(Date.now() - 2 * 60 * 60 * 1000 + 60_000),
      duration: 60,
      overallScore: 7,
      transcript: '大家好，我是来参观的。这个项目很有意思。',
      segments: {
        create: [
          {
            ts: 0,
            duration: 60,
            fluencyScore: 7,
            logicScore: 7,
            paceScore: 7,
            fillerCount: 1,
            pauseCount: 1,
            text: '大家好，我是来参观的。这个项目很有意思。',
          },
        ],
      },
      hookEvents: {
        create: [],
      },
    },
  });
  console.log(`  ✅ 参观者会话已创建`);

  console.log('');
  console.log('🎉 播种完成！');
  console.log('');
  console.log('  演示用户 ID: demo-user-001');
  console.log('  访问画像页: http://localhost:5173/profile?userId=demo-user-001');
  console.log('  访问凭证页: http://localhost:5173/credentials?userId=demo-user-001');
}

main()
  .catch((err) => {
    console.error('❌ 播种失败:', err);
    process.exit(1);
  })
  .finally(async () => {
    await prisma.$disconnect();
  });
