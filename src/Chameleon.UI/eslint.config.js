import js from '@eslint/js';
import reactHooks from 'eslint-plugin-react-hooks';
import globals from 'globals';
import tseslint from 'typescript-eslint';

export default tseslint.config(
  { ignores: ['dist', 'node_modules', 'e2e/.out', 'playwright-report', 'test-results'] },
  js.configs.recommended,
  ...tseslint.configs.recommended,
  reactHooks.configs.flat['recommended-latest'],
  {
    files: ['**/*.{ts,tsx}'],
    languageOptions: { globals: { ...globals.browser } },
    rules: {
      '@typescript-eslint/no-explicit-any': 'error',
      '@typescript-eslint/consistent-type-imports': 'error',
      '@typescript-eslint/no-unused-vars': ['error', { argsIgnorePattern: '^_' }],
      // All host communication goes through src/bridge.
      'no-restricted-globals': [
        'error',
        { name: 'fetch', message: 'Talk to the host through the bridge (src/bridge), not fetch.' },
        { name: 'EventSource', message: 'Subscribe through the bridge (src/bridge).' },
      ],
    },
  },
  {
    files: ['src/bridge/**', 'e2e/**', '*.config.ts', 'src/test/**'],
    rules: { 'no-restricted-globals': 'off' },
  },
  {
    files: ['e2e/**', '*.config.ts', '*.config.js'],
    languageOptions: { globals: { ...globals.node } },
    rules: { 'react-hooks/rules-of-hooks': 'off' },
  },
);
