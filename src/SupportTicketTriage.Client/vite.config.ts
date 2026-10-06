import react from '@vitejs/plugin-react'
import { defineConfig } from 'vitest/config'

export default defineConfig({
  plugins: [react()],

  // The app only ever calls relative /api paths, so the browser and the API
  // share an origin and the API needs no CORS configuration at all. Vite
  // forwards those calls in development; nginx does the same job in the
  // container. The alternative - calling http://localhost:8080 directly -
  // would mean shipping a permissive CORS policy that exists only because of
  // a development-time choice.
  server: {
    proxy: {
      '/api': {
        target: 'http://localhost:8080',
        changeOrigin: true,
        rewrite: (path) => path.replace(/^\/api/, ''),
      },
    },
  },

  test: {
    environment: 'jsdom',
    globals: true,
    setupFiles: './src/test-setup.ts',
  },
})
