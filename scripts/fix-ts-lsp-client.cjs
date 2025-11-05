#!/usr/bin/env node

const fs = require("fs");
const path = require("path");

const pkgPath = path.join(__dirname, "..", "node_modules", "ts-lsp-client", "package.json");

try {
  const raw = fs.readFileSync(pkgPath, "utf8");
  const json = JSON.parse(raw);

  if (json.type === "module") {
    delete json.type;
    fs.writeFileSync(pkgPath, JSON.stringify(json, null, 2) + "\n", "utf8");
    console.log("[postinstall] Removed type=\"module\" from ts-lsp-client to enable CommonJS exports.");
  }
} catch (error) {
  if (error.code !== "ENOENT") {
    console.error("[postinstall] Failed to adjust ts-lsp-client package.json:", error);
    process.exitCode = 0;
  }
}
