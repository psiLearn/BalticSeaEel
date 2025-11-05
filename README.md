# SAFE Snake

A SAFE-stack Snake game with a Giraffe API backend and a Fable/Elmish client bundled via Vite.

## Prerequisites

- .NET 8 SDK (`dotnet --version`)
- Node.js 18+ with npm (`npm --version`)

## Getting started

```bash
npm install
dotnet tool restore
```

The `.gitignore` file excludes Vite artifacts (`dist`, `src/Server/wwwroot/*`) and `node_modules`. The tracked `.gitkeep` keeps the static folder in source control while letting builds emit fresh assets.

## Development

```bash
npm run dev          # launches Vite on http://localhost:5173 with /api proxy
dotnet watch run --project src/Server/Server.fsproj
```

Open the Vite URL in a browser; API calls are proxied to the Giraffe server running on `https://localhost:5001` / `http://localhost:5000`.

## Production build

```bash
npm run build        # builds client assets into src/Server/wwwroot
dotnet publish src/Server/Server.fsproj
```

The publish output contains both the compiled server and the static bundle produced by Vite.
