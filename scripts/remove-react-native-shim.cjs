#!/usr/bin/env node

const fs = require("fs");
const path = require("path");

const projectRoot = path.resolve(__dirname, "..");
const targetFile = "react-native.fs.js";

const searchRoots = [
  "src/Client/fable_modules",
  "tests/Client.FableTests/fable_modules",
];

const removed = [];

for (const relativeRoot of searchRoots) {
  const absoluteRoot = path.join(projectRoot, relativeRoot);
  if (!fs.existsSync(absoluteRoot)) {
    continue;
  }

  for (const entry of fs.readdirSync(absoluteRoot)) {
    if (!entry.toLowerCase().startsWith("fable.elmish.react")) {
      continue;
    }

    const candidate = path.join(absoluteRoot, entry, targetFile);
    if (fs.existsSync(candidate)) {
      fs.rmSync(candidate, { force: true });
      removed.push(path.relative(projectRoot, candidate));
    }
  }
}

if (removed.length > 0) {
  console.log("[remove-react-native-shim] removed files:");
  for (const file of removed) {
    console.log(` - ${file}`);
  }
} else {
  console.log("[remove-react-native-shim] no shim files found (nothing to do).");
}
