import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

export default defineConfig({
    plugins: [react()],
    build: {
        outDir: "../../deploy/public",
        emptyOutDir: true,
    },
    clearScreen: false,
});
