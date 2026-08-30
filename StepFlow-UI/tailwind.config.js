/** @type {import('tailwindcss').Config} */
export default {
  content: [
    './index.html',
    './src/**/*.{js,ts,jsx,tsx}',
  ],
  darkMode: 'class',
  theme: {
    extend: {
      colors: {
        // Category accent colors
        accent: {
          ai: '#8B5CF6',
          rule: '#F59E0B',
          data: '#3B82F6',
          api: '#10B981',
          transform: '#EC4899',
          utility: '#6B7280',
          subflow: '#06B6D4',
        },
        // Canvas theme
        canvas: {
          bg: '#0A0A1A',
          grid: '#1A1A2E',
          gridDot: '#2A2A4A',
        },
        // Node colors
        node: {
          bg: '#1E1E3A',
          border: '#3A3A5C',
          borderSelected: '#6366F1',
          borderInvalid: '#EF4444',
          borderValid: '#22C55E',
          borderWarning: '#F59E0B',
        },
        // Palette colors
        palette: {
          bg: '#16213E',
          hover: '#1E3A5F',
          active: '#2A4A7F',
        },
      },
      fontFamily: {
        sans: ['-apple-system', 'BlinkMacSystemFont', 'Segoe UI', 'Roboto', 'sans-serif'],
        mono: ['JetBrains Mono', 'Fira Code', 'Consolas', 'monospace'],
      },
      animation: {
        'pulse-slow': 'pulse 3s cubic-bezier(0.4, 0, 0.6, 1) infinite',
        'flow': 'flow 2s linear infinite',
      },
      keyframes: {
        flow: {
          '0%': { strokeDashoffset: '0' },
          '100%': { strokeDashoffset: '-20' },
        },
      },
    },
  },
  plugins: [
    require('@tailwindcss/typography'),
    require('@tailwindcss/forms'),
    require('@tailwindcss/container-queries'),
  ],
}
