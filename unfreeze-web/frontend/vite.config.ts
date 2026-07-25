import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// https://vitejs.dev/config/
export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      // REST API 代理到后端
      '/api': {
        target: 'http://localhost:3001',
        changeOrigin: true,
      },
      // WebSocket 代理到后端（含 ws 升级）
      '/ws': {
        target: 'ws://localhost:3001',
        ws: true,
        changeOrigin: true,
      },
    },
  },
});
