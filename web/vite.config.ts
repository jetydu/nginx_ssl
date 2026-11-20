import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'

export default defineConfig({
  plugins: [vue()],
  server: {
    port: 5173,
    host: '0.0.0.0',
    proxy: {
      '/api': {
        target: 'http://127.0.0.1:5188',
        changeOrigin: true,
      },
    },
  },
  build: {
    outDir: '../src/NginxSslManager.Api/wwwroot',
    emptyOutDir: true,
    chunkSizeWarningLimit: 1200,
  },
})
