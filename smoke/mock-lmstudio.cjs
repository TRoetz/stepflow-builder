// Minimal OpenAI-compatible mock of LM Studio for smoke-testing the
// browser's local AI execution path. Returns:
//   - decision prompts  -> {"decision": true}
//   - other prompts     -> plain text
// Serves GET /api/v1/models and POST /v1/chat/completions with CORS.
const http = require('http');

const server = http.createServer((req, res) => {
  res.setHeader('Access-Control-Allow-Origin', '*');
  res.setHeader('Access-Control-Allow-Headers', 'content-type, authorization, api-key, x-api-key, anthropic-version');
  res.setHeader('Access-Control-Allow-Methods', 'GET, POST, OPTIONS');
  if (req.method === 'OPTIONS') {
    res.writeHead(204);
    res.end();
    return;
  }
  let body = '';
  req.on('data', (c) => (body += c));
  req.on('end', () => {
    if (req.method === 'GET' && req.url === '/api/v1/models') {
      res.writeHead(200, { 'Content-Type': 'application/json' });
      res.end(JSON.stringify({ object: 'list', data: [{ id: 'llama3.2', object: 'model' }] }));
      return;
    }
    if (req.method === 'POST' && req.url === '/v1/chat/completions') {
      const parsed = JSON.parse(body || '{}');
      const system = (parsed.messages || []).find((m) => m.role === 'system')?.content || '';
      const content = /decision/i.test(system) ? '{"decision": true}' : 'Smoke test: the order is ready to ship.';
      console.log('MOCK-LMSTUDIO chat model=' + (parsed.model || '?') + ' user=' + ((parsed.messages || []).find((m) => m.role === 'user')?.content || '').slice(0, 60));
      res.writeHead(200, { 'Content-Type': 'application/json' });
      res.end(
        JSON.stringify({
          id: 'smoke-1',
          object: 'chat.completion',
          created: 1,
          model: parsed.model || 'llama3.2',
          choices: [{ index: 0, message: { role: 'assistant', content }, finish_reason: 'stop' }],
          usage: { prompt_tokens: 10, completion_tokens: 5, total_tokens: 15 },
        })
      );
      return;
    }
    res.writeHead(404, { 'Content-Type': 'application/json' });
    res.end(JSON.stringify({ error: { message: 'not found: ' + req.url } }));
  });
});

server.listen(1234, '127.0.0.1', () => console.log('MOCK-LMSTUDIO listening on 1234'));
