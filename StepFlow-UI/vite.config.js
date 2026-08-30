import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import path from 'path';
export default defineConfig({
    plugins: [react()],
    resolve: {
        alias: {
            '@': path.resolve(__dirname, './src'),
            '@components': path.resolve(__dirname, './src/components'),
            '@stores': path.resolve(__dirname, './src/stores'),
            '@schemas': path.resolve(__dirname, './src/schemas'),
            '@library': path.resolve(__dirname, './src/library'),
            '@hooks': path.resolve(__dirname, './src/hooks'),
            '@services': path.resolve(__dirname, './src/services'),
            '@types': path.resolve(__dirname, './src/types'),
            '@utils': path.resolve(__dirname, './src/utils'),
            '@styles': path.resolve(__dirname, './src/styles'),
        },
    },
    build: {
        // Standalone frontend output: StepFlow-UI/dist/. The docker-example nginx image serves this;
        // for single-process mode copy dist/ next to the backend as StepFunctionsApp/dist (Program.cs still serves it).
        outDir: './dist',
        emptyOutDir: true,
        rollupOptions: {
            input: {
                main: './index.html',
            },
        },
    },
    server: {
        port: 3001,
        strictPort: true,
        proxy: {
            '/api': {
                target: 'http://localhost:5001',
                changeOrigin: true,
                secure: false,
            },
        },
    },
});
