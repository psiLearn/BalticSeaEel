# Baltic Sea Eel

Baltic Sea Eel is a SAFE-stack take on the classic snake formula. Each eel segment traces vocabulary drawn from a backend word list, so spelling the prompted phrase becomes part of the challenge. The project is built with a Giraffe API, a Fable/Elmish client, and Vite for bundling.

## Features

- Vocabulary-driven goals – every piece of food advances the current word or example sentence.
- Letter trail – collected characters appear directly on the eel’s body for an at-a-glance progress check.
- Dynamic pacing – the game speeds up after phrases are completed and resets to the base tempo on restart.
- Persistent scores – submit finished runs to the high-score endpoint once the game ends.
- Baltic-themed safety net – if a vocabulary entry is empty, the eel spells the fallback phrase “Baltc Sea Eel”.

## Controls

- `Arrow` keys or `WASD` to steer the eel.
- `Space` to restart after a game-over or whenever you want to reset.

## Prerequisites

- .NET 8 SDK (`dotnet --version`)
- Node.js 18+ with npm (`npm --version`)

## Setup

```bash
npm install
dotnet tool restore
```

The tracked `.gitkeep` keeps `wwwroot` in source control while allowing builds to emit fresh assets; `dist` and `node_modules` remain ignored.

## Development

```bash
npm run dev          # Vite dev server on http://localhost:5173 with API proxying
dotnet watch run --project src/Server/Server.fsproj
```

Browse to the Vite address; requests to `/api/*` are forwarded to `https://localhost:5001` / `http://localhost:5000`.

## Production build

```bash
npm run build        # bundles client assets into src/Server/wwwroot
dotnet publish src/Server/Server.fsproj
```

The publish output contains both the compiled server and the static bundle produced by Vite.
