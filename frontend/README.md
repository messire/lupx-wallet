# LupexWalletWeb

This project was generated using [Angular CLI](https://github.com/angular/angular-cli) version 22.1.3.

## Development server

To start a local development server, run:

```bash
ng serve
```

Once the server is running, open your browser and navigate to `http://localhost:4200/`. The application will automatically reload whenever you modify any of the source files.

## Frontend in Docker (without running it in the IDE)

If you don't need to debug the frontend in the IDE (e.g. you're debugging the backend instead),
it can run in Docker alongside postgres — see `docker-compose.frontend.yml` in the repository
root and the [«Независимый запуск backend/frontend в Docker»](../README.md#независимый-запуск-backendfrontend-в-docker)
section of the root README (in Russian, per project convention):

```bash
docker compose -f docker-compose.yml -f docker-compose.frontend.yml up -d --build
```

Serves on the same `http://localhost:4200/` as `npm start`.

## Code scaffolding

Angular CLI includes powerful code scaffolding tools. To generate a new component, run:

```bash
ng generate component component-name
```

For a complete list of available schematics (such as `components`, `directives`, or `pipes`), run:

```bash
ng generate --help
```

## Building

To build the project run:

```bash
ng build
```

This will compile your project and store the build artifacts in the `dist/` directory. By default, the production build optimizes your application for performance and speed.

## Running unit tests

To execute unit tests with the [Vitest](https://vitest.dev/) test runner, use the following command:

```bash
ng test
```

## Running end-to-end tests

For end-to-end (e2e) testing, run:

```bash
ng e2e
```

Angular CLI does not come with an end-to-end testing framework by default. You can choose one that suits your needs.

## Additional Resources

For more information on using the Angular CLI, including detailed command references, visit the [Angular CLI Overview and Command Reference](https://angular.dev/tools/cli) page.

## Deploy (Vercel)

Topology and rationale — [ADR-0013](../docs/architecture/adr/0013-deployment-topology.md) (in Russian, per project convention).

`vercel.json` sets the build command to `npm run build:deploy`, which runs `scripts/write-env.mjs` before `ng build` — that script writes `src/environments/environment.ts` from the `API_BASE_URL` environment variable (set per Vercel environment/branch, pointing at the matching Railway backend). Output directory: `dist/lupex-wallet-web/browser`. Client-side routing is handled by a catch-all rewrite to `index.html`.

Required environment variable in Vercel project settings: `API_BASE_URL` (full backend URL, e.g. `https://lupex-wallet-api.up.railway.app/api/v1`) — set separately for Production and Preview/branch environments, since prod and staging point at different Railway deployments.
