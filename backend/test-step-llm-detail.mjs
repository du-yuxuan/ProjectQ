// 阶跃星辰 LLM 完整响应检查
import dotenv from 'dotenv';
import { fileURLToPath } from 'url';
import { dirname, join } from 'path';

const __dirname = dirname(fileURLToPath(import.meta.url));
dotenv.config({ path: join(__dirname, '.env') });

async function main() {
  console.log('API Key:', process.env.STEPAUDIO_API_KEY?.slice(0, 10) + '...');

  const resp = await fetch('https://api.stepfun.com/v1/chat/completions', {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      Authorization: 'Bearer ' + process.env.STEPAUDIO_API_KEY,
    },
    body: JSON.stringify({
      model: 'step-3.7-flash',
      messages: [
        { role: 'system', content: '你是演讲评估专家。请输出JSON：{"logic": 8, "reason": "结构清晰"}' },
        { role: 'user', content: '首先我们做了一个产品。然后用户很喜欢。因此方向是对的。' },
      ],
      temperature: 0.3,
      max_tokens: 256,
    }),
  });

  console.log('HTTP:', resp.status);
  const data = await resp.json();

  if (data.error) {
    console.log('Error:', JSON.stringify(data.error));
    return;
  }

  const choice = data.choices?.[0];
  console.log('finish_reason:', choice?.finish_reason);
  console.log('message.role:', choice?.message?.role);
  console.log('message.content:', JSON.stringify(choice?.message?.content));
  console.log('message.reasoning_content:', JSON.stringify(choice?.message?.reasoning_content?.slice(0, 200)));
  console.log('usage:', JSON.stringify(data.usage));
  console.log('full message keys:', Object.keys(choice?.message || {}));
}

main().catch(console.error);
