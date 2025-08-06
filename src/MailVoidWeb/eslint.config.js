// @ts-check
const typescript = require('@typescript-eslint/eslint-plugin');
const tsParser = require('@typescript-eslint/parser');
const angular = require('@angular-eslint/eslint-plugin');
const angularTemplate = require('@angular-eslint/eslint-plugin-template');
const templateParser = require('@angular-eslint/template-parser');
const prettier = require('eslint-plugin-prettier');
const prettierConfig = require('eslint-config-prettier');

module.exports = [
  {
    ignores: ['dist/', 'node_modules/', '.angular/', '.cache/', '.git/', '.github/', 'coverage/', '**/*.d.ts'],
  },
  // TypeScript files
  {
    files: ['**/*.ts'],
    languageOptions: {
      parser: tsParser,
      parserOptions: {
        project: ['./tsconfig.json', './projects/*/tsconfig.*.json'],
        createDefaultProgram: true,
      },
    },
    plugins: {
      '@typescript-eslint': typescript,
      '@angular-eslint': angular,
      prettier,
    },
    rules: {
      // TypeScript recommended rules
      ...typescript.configs.recommended.rules,
      // Angular recommended rules
      ...angular.configs.recommended.rules,
      // Prettier rules
      ...prettierConfig.rules,
      'prettier/prettier': 'error',

      // Angular-specific rules
      '@angular-eslint/directive-selector': [
        'error',
        {
          type: 'attribute',
          prefix: 'app',
          style: 'camelCase',
        },
      ],
      '@angular-eslint/component-selector': [
        'error',
        {
          type: 'element',
          style: 'kebab-case',
        },
      ],
      '@angular-eslint/prefer-standalone': 'warn',
      '@angular-eslint/prefer-on-push-component-change-detection': 'warn',

      // TypeScript rules customization
      '@typescript-eslint/no-explicit-any': 'warn',
      '@typescript-eslint/no-unused-vars': ['error', { argsIgnorePattern: '^_' }],
      '@typescript-eslint/explicit-function-return-type': 'off',
      '@typescript-eslint/explicit-module-boundary-types': 'off',
      '@typescript-eslint/no-inferrable-types': 'off',
      '@typescript-eslint/ban-types': 'off',

      // Disabled rules for legacy code compatibility
      '@angular-eslint/no-host-metadata-property': 'off',
      '@angular-eslint/no-output-on-prefix': 'off',
      '@typescript-eslint/member-ordering': 'off',
      '@typescript-eslint/naming-convention': 'off',
    },
  },
  // Angular templates
  {
    files: ['**/*.html'],
    languageOptions: {
      parser: templateParser,
    },
    plugins: {
      '@angular-eslint/template': angularTemplate,
      prettier,
    },
    rules: {
      // Angular template recommended rules
      ...angularTemplate.configs.recommended.rules,
      // Prettier for templates
      'prettier/prettier': ['error', { parser: 'angular' }],

      // Custom template rules
      '@angular-eslint/template/prefer-self-closing-tags': 'error',
      '@angular-eslint/template/conditional-complexity': ['error', { maxComplexity: 3 }],
      '@angular-eslint/template/cyclomatic-complexity': ['error', { maxComplexity: 5 }],
      '@angular-eslint/template/use-track-by-function': 'error',
    },
  },
  // Special overrides for app.component.ts files
  {
    files: ['**/app.component.ts'],
    rules: {
      '@angular-eslint/prefer-standalone': 'off',
      '@angular-eslint/prefer-on-push-component-change-detection': 'off',
    },
  },
];
