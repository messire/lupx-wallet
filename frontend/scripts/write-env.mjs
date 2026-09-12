// Writes src/environments/environment.ts before the production build, so the
// same build command works for both Railway environments (prod/staging) —
// Vercel injects a different API_BASE_URL per environment/branch.
// Falls back to the existing relative "/api/v1" (same-origin) when the
// variable isn't set, so a plain `ng build` outside Vercel is unaffected.
import { writeFileSync } from "node:fs";
import { resolve } from "node:path";

const apiBaseUrl = process.env.API_BASE_URL ?? "/api/v1";
const targetPath = resolve(import.meta.dirname, "..", "src", "environments", "environment.ts");

const content = `export const environment = {
  production: true,
  apiBaseUrl: '${apiBaseUrl}',
};
`;

writeFileSync(targetPath, content);
console.log(`environment.ts written with apiBaseUrl = ${apiBaseUrl}`);
