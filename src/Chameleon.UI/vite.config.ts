import react from '@vitejs/plugin-react';
import { defineConfig } from 'vitest/config';

// The bundle runs inside WebView2 (Windows) and WKWebView (macOS, Safari 15+), served from
// http://127.0.0.1:<port>/<token>/, so every asset URL must be relative.
export default defineConfig({
  base: './',
  plugins: [react()],
  build: {
    target: ['chrome100', 'safari15'],
    outDir: 'dist',
    emptyOutDir: true,
    sourcemap: false,
  },
  server: {
    host: 'localhost',
    port: 5173,
    strictPort: true,
  },
  test: {
    environment: 'jsdom',
    include: ['src/**/*.test.{ts,tsx}'],
    setupFiles: ['src/test/setup.ts'],
    css: { modules: { classNameStrategy: 'non-scoped' } },
  },
});
