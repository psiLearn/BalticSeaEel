import { defineConfig } from "vite";
import fable from "vite-plugin-fable";

export default defineConfig({
  root: "./src/Client",
  plugins: [
    fable({
      fsproj: "./src/Client/Client.fsproj"
    })
  ],
  build: {
    outDir: "../Server/wwwroot",
    emptyOutDir: true
  },
  server: {
    port: 5173,
    proxy: {
      "/api": {
        target: "http://localhost:5000",
        changeOrigin: true
      }
    }
  }
});
