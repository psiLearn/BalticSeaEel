# Baltic Sea Eel

Baltic Sea Eel is a SAFE-stack take on the classic snake formula. Each eel segment traces vocabulary drawn from a backend word list, so spelling the prompted phrase becomes part of the challenge. The project is built with a Giraffe API, a Fable/Elmish client, and Vite for bundling.

## Features

- Vocabulary-driven goals – every piece of food advances the current word or example sentence.
- Letter trail – collected characters appear directly on the eel’s body for an at-a-glance progress check.
- Dynamic pacing – the game speeds up after phrases are completed and resets to the base tempo on restart.
- Persistent scores – submit finished runs to the high-score endpoint once the game ends.
- Baltic-themed safety net – if a vocabulary entry is empty, the eel spells the fallback phrase “Baltc Sea Eel”.

## Controls

- `Arrow` keys or `WASD` to steer the eel on desktop.
- Swipe anywhere on touch devices to change direction (swipe direction = turn direction).
- `Space` to restart after a game-over or whenever you want to reset.

## Prerequisites

- .NET 8 SDK (`dotnet --version`)
- Node.js 18+ with npm (`npm --version`)
- PostgreSQL 15+ (local or containerised). The API reads the connection string from the `POSTGRES_CONNECTION` environment variable; if unset it defaults to `Host=localhost;Port=5432;Username=eel;Password=eel;Database=eel`.

### Local stack via Docker Compose

```bash
docker compose up --build
```

The compose file builds the app image (client + server) and brings up Postgres with the default `eel/eel` credentials. Browse to `http://localhost:5000` and the server will use the internal connection string `Host=postgres;Port=5432;Username=eel;Password=eel;Database=eel`.

### Seeding vocabulary entries

Once the container is running you can seed the `vocabulary` table with the helper script:

```powershell
# Windows PowerShell
psql -h localhost -U eel -d eel -f scripts/seed-vocabulary.sql

# macOS/Linux
psql -h localhost -U eel -d eel -f scripts/seed-vocabulary.sql
```

For larger CSV batches, run the helper script:

```powershell
pwsh scripts/import-vocabulary.ps1 `
  -CsvPath vocabularies/french_vocabularies_balticsea.csv `
  -Connection "Host=localhost;Port=5432;Username=eel;Password=eel;Database=eel"
```

To insert a single entry by hand:

```sql
INSERT INTO vocabulary (topic, language1, language2, example)
VALUES ('Transport', 'le bateau', 'das Schiff', 'Le bateau part à midi.');
```

The API still exposes the in-memory fallback list, but any entries in the `vocabulary` table are ready for future use or reporting.

## Setup

```powershell
npm install
dotnet tool restore
```

The tracked `.gitkeep` keeps `wwwroot` in source control while allowing builds to emit fresh assets; `dist` and `node_modules` remain ignored.

### Description

Baltic Sea Eel pairs a Fable/Elmish client with a Giraffe API to build a vocabulary-focused twist on Snake. The eel’s body spells vocabulary fetched from the server, so collecting food advances letters while the backend keeps a persistent scoreboard in PostgreSQL. The repo includes:

- `src/Client`: Elmish update loop, React view, and integration tests via `npm run test:client:fable`.
- `src/Server`: Giraffe endpoints, Postgres-backed high-score service, and vocabulary module (currently seeded from F# but ready for DB-backed entries).
- `tests/`: xUnit suites for both client and server plus Fable/Mocha tests for pure Elmish logic.

With Docker you can spin up the Postgres container locally, seed vocabulary, and run both frontend and backend together via `npm run dev` and `dotnet watch run`.

### Visual & highlight configuration

Most of the rendering behaviour can be tweaked centrally in `src/Shared/Config.fs`:

- `FoodVisuals` controls whether food letters are shown at all and which colours are used for active/collected tokens (default is to hide the letters for a cleaner board).
- `BoardVisuals` sets the grid background colour, highlight opacity, and board-letter styling.
- `FoodBurst` enables/disables the eel highlight wave, caps how many simultaneous waves may run, and scales their speed/brightness.

Example:

```fsharp
FoodVisuals =
    { ShowLetters = true
      ActiveFill = "rgba(88,161,107,0.8)"
      CollectedFill = "rgba(255,255,255,0.15)" }
FoodBurst =
    { Enabled = true
      MaxConcurrentWaves = Some 3
      WaveSpeedSegmentsPerMs = 0.001
      LetterSizeFactor = 1.5
      LetterWeightFactor = 1.3
      SegmentWeightFactor = 0.8 }
```

Adjust these values and reload the client to experiment with different looks or to disable specific effects (e.g., set `FoodVisuals.ShowLetters = true` to restore letters on food tokens).

## Development

```powershell
npm run dev          # Vite dev server on http://localhost:5173 with API proxying
dotnet watch run --project src/Server/Server.fsproj
```

Browse to the Vite address; requests to `/api/*` are forwarded to `https://localhost:5001` / `http://localhost:5000`.

> **PostgreSQL connection:** when running the server, point it at your database via `POSTGRES_CONNECTION`. Example:
>
> ```bash
> # Windows PowerShell
> $env:POSTGRES_CONNECTION="Host=localhost;Port=5432;Username=eel;Password=eel;Database=eel"
> dotnet watch run --project src/Server/Server.fsproj
>
> # macOS/Linux
> export POSTGRES_CONNECTION="Host=localhost;Port=5432;Username=eel;Password=eel;Database=eel"
> dotnet watch run --project src/Server/Server.fsproj
> ```

## Production build

```powershell
npm run build        # bundles client assets into src/Server/wwwroot
dotnet publish src/Server/Server.fsproj
```

The publish output contains both the compiled server and the static bundle produced by Vite.

### Docker image

```bash
docker build -t eel-app .
docker run -p 5000:5000 `
  -e POSTGRES_CONNECTION="Host=localhost;Port=5432;Username=eel;Password=eel;Database=eel" `
  eel-app
```

### AWS Infrastructure

`terraform/aws` contains a baseline stack for AWS (VPC, Aurora Postgres, ECS/Fargate, ALB, Secrets Manager, ECR). Before running Terraform:

1. Update the `backend "s3"` block in `main.tf` with your state bucket/key/region.
2. Provide AWS credentials (env vars or profile).
3. Run `terraform init` and `terraform apply -var="project=eel" -var="db_username=eel" -var="db_password=..."`.

Push your Docker image to the ECR repo Terraform creates, then update the ECS service/task definition with the new tag. The ECS task reads `POSTGRES_CONNECTION` from Secrets Manager.

## Testing and coverage

```powershell
dotnet test                    # run the xUnit suite
dotnet test --settings tests/coverlet.runsettings --collect:"XPlat Code Coverage"
dotnet test tests/Server.Tests/Server.Tests.fsproj   # server-only test run
npm run test:client:fable      # run client tests in a browser-like JS runtime (Fable + Mocha)
```

Coverage reports are emitted to `tests/Client.Tests/TestResults/<run-id>/` in both Cobertura and LCOV formats for use in IDEs or CI tooling.

To combine reports from multiple suites (e.g., .NET + mocha/nyc), make each suite export the same format (Cobertura or LCOV) and merge them with [ReportGenerator](https://github.com/danielpalme/ReportGenerator):

```powershell
# example: merge .NET Cobertura + JS Cobertura
reportgenerator `
  "-reports:tests/Client.Tests/TestResults/**/coverage.cobertura.xml;path/to/js/coverage.cobertura.xml" `
  "-targetdir:coveragereport" `
  "-reporttypes:Html"
```

For JS/Fable tests, wrap `npm run test:client:fable` with [nyc](https://github.com/istanbuljs/nyc) or another coverage runner so it emits LCOV/Cobertura files that the merge step can consume.

### Progressive Web App build & install

1. **Build exactly like production** – `npm run build` already emits everything needed for the PWA: the hashed bundles, Workbox precache manifest, and `dist/sw.js`. There is no extra script; just run the standard build and host the `dist` output.
2. **Serve over HTTPS when testing** – install prompts and service workers require a secure origin. Quick options:
   - Generate a throwaway self-signed cert once (PowerShell example):

     ```powershell
     openssl req -x509 -nodes -days 365 -newkey rsa:2048 `
       -keyout localhost-key.pem `
       -out localhost-cert.pem `
       -subj "/CN=localhost"
     ```

     Then run `npx http-server dist --ssl --cert localhost-cert.pem --key localhost-key.pem` for local smoke tests.
   - Deploy the contents of `dist` to GitHub Pages, Azure Static Web Apps, Netlify, etc.
   - Use Chrome DevTools’ Lighthouse PWA audit to verify offline caching/install readiness.
3. **Installation guidance** – document the platform-specific flow for testers:
   - **Android Chrome / Edge**: open the overflow menu → *Install app*.
   - **iOS Safari**: tap Share → *Add to Home Screen*.
   - **Desktop Chromium (Chrome, Edge)**: click the install icon in the omnibox.
   - **Android Emulator**: launch the emulator (via Android Studio), open Chrome inside the emulated device, and browse to your HTTPS dev URL (e.g. `https://10.0.2.2:8443` if you’re serving with `http-server --ssl`). Once the page loads, tap the overflow menu → *Install app* just like on a real phone.
   - **iOS Simulator**: run the simulator via Xcode, open Safari to the HTTPS dev URL, and use Share → *Add to Home Screen*.
4. **Offline expectations** – the Workbox configuration precaches the immutable bundles and keeps `/api` calls `NetworkFirst`. Users can continue playing with cached assets, and the game will refresh scores once connectivity returns.
5. **MAUI & PWA parity** – the Elmish client is shared between MAUI and the PWA bundle. Run both builds before a release to ensure gameplay stays identical, even if you lean on native shells for distribution.

### Tooling installation overview

| Tool / SDK            | Purpose                                     | Quick installation                                                                 |
|-----------------------|---------------------------------------------|-------------------------------------------------------------------------------------|
| Node.js 18+ / npm     | Vite dev server, client bundling            | <https://nodejs.org> (LTS installer)                                                 |
| .NET 8 SDK            | Server build/tests, Elmish tooling          | <https://dotnet.microsoft.com/download/dotnet/8.0>                                   |
| PostgreSQL 15+        | Persistent scores / vocabulary              | <https://www.postgresql.org/download/> (or Docker image `postgres:15`)               |
| OpenSSL (optional)    | Self-signed cert for local HTTPS testing    | Use Git Bash/WSL or <https://slproweb.com/products/Win32OpenSSL.html>                |
| SonarScanner for .NET | Static analysis (optional)                  | `dotnet tool install --global dotnet-sonarscanner`                                 |
| Android Studio / Xcode| Emulator/simulator for install testing      | <https://developer.android.com/studio> / <https://developer.apple.com/xcode/>          |

### SonarQube analysis

1. Start a SonarQube instance. The repo includes a sample compose file under `c:\Tools\sonarqube-25.11.0.114957\sonar-stack\docker-compose.yml`:

   ```powershell
   docker compose -f c:\Tools\sonarqube-25.11.0.114957\sonar-stack\docker-compose.yml up -d
   ```

   Browse to `http://localhost:9000`, create an admin token (e.g., `ALE_SONAR`), and keep it handy.
2. Install the .NET scanner if needed:

   ```powershell
   dotnet tool install --global dotnet-sonarscanner
   ```

3. Run the analysis from the repo root (replace the key/name/token to match your project):

   ```powershell
   $env:SONAR_TOKEN="your-generated-token"

   dotnet sonarscanner begin `
     /k:"Ale" `
     /n:"Baltic Sea Eel" `
     /d:sonar.host.url="http://localhost:9000" `
     /d:sonar.login=$env:SONAR_TOKEN

   dotnet build

   dotnet sonarscanner end /d:sonar.login=$env:SONAR_TOKEN
   ```

   The scanner writes its intermediate files under `.sonarqube/` (already ignored). After the `end` step, open the SonarQube UI to inspect quality gates, code smells, and coverage imported from `dotnet test`.

### API documentation

Swagger/OpenAPI is generated automatically via [Giraffe.OpenApi](https://github.com/giraffe-fsharp/Giraffe.OpenApi). When the server runs in development mode, browse to `https://localhost:5001/swagger` (or `http://localhost:5000/swagger`) to inspect the live contract.

### CORS configuration

The server reads allowed origins from `src/Server/appsettings.json` (`Cors:AllowedOrigins`). Update that list for Docker/production deployments to match the domains serving the SPA.

### Renderer toggle (canvas vs Konva)

The board now supports both the original Canvas renderer and an experimental [Konva](https://konvajs.org/) renderer. By default the Canvas renderer is used. To switch to Konva in the browser, set `window.__RENDER_ENGINE__ = "konva"` before the app bootstraps (for example, via a small inline script in `index.html`). The Konva path shares the same render helpers, so it’s safe to experiment without affecting gameplay logic.

### VS Code integration

- Install the recommended workspace extensions and open the Testing panel to discover both `Client.Tests` and `Server.Tests`.
- Use the built-in Test tasks (`test:client`, `test:server`, `test:coverage`, `test:client:fable`) from the command palette (`Tasks: Run Task`) for quick execution inside the editor.
