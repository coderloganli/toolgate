import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// The dev server proxies API calls to the gateway so the console and API share an origin.
const apiTarget = process.env.TOOLGATE_API_URL ?? 'http://localhost:8080'

export default defineConfig({
  plugins: [react()],
  server: {
    proxy: {
      '/api': apiTarget,
      '/dev': apiTarget,
    },
  },
})
