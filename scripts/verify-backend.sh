#!/bin/bash
lsof -ti:3001 | xargs kill -9 2>/dev/null
sleep 1
cd /Volumes/Workbench/UnF/Q/backend
npx tsx src/app.ts &
SERVER_PID=$!
sleep 4
curl -s http://localhost:3001/api/health
echo ""
kill $SERVER_PID 2>/dev/null
wait $SERVER_PID 2>/dev/null
