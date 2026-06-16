import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// In dev, proxy API calls to the ASP.NET backend (see backend launchSettings, port 5180).
export default defineConfig({
  plugins: [react()],
  server: {
    proxy: {
      '/api': 'http://localhost:5180',
    },
  },
})
