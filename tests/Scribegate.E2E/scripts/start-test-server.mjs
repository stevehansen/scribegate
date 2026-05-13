// Build the SPA into the Web project's wwwroot, then launch the ASP.NET host
// against an isolated temp data directory. Playwright probes /healthz before
// running specs.
//
// Env:
//   PLAYWRIGHT_PORT  port the host should listen on (defaults to 5099)
//   SKIP_CLIENT_BUILD=1  reuse the existing wwwroot (handy for fast local re-runs)

import { spawn } from 'node:child_process';
import { existsSync, mkdirSync, mkdtempSync, rmSync } from 'node:fs';
import { cp } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const __dirname = dirname(fileURLToPath(import.meta.url));
const repoRoot = resolve(__dirname, '..', '..', '..');
const clientDir = join(repoRoot, 'src', 'Scribegate.Web', 'Client');
const webProject = join(repoRoot, 'src', 'Scribegate.Web');
const wwwroot = join(webProject, 'wwwroot');
const clientDist = join(clientDir, 'dist');

const port = Number(process.env.PLAYWRIGHT_PORT ?? 5099);
const dataDir = mkdtempSync(join(tmpdir(), 'scribegate-e2e-'));

function run(cmd, args, opts = {}) {
  return new Promise((resolveRun, rejectRun) => {
    const proc = spawn(cmd, args, {
      stdio: 'inherit',
      shell: process.platform === 'win32',
      ...opts,
    });
    proc.on('exit', (code) => {
      if (code === 0) resolveRun();
      else rejectRun(new Error(`${cmd} ${args.join(' ')} exited with code ${code}`));
    });
    proc.on('error', rejectRun);
  });
}

async function buildClient() {
  if (process.env.SKIP_CLIENT_BUILD === '1' && existsSync(join(wwwroot, 'index.html'))) {
    console.log('[e2e] SKIP_CLIENT_BUILD=1 — reusing existing wwwroot');
    return;
  }
  console.log('[e2e] npm ci (client)');
  await run('npm', ['ci', '--ignore-scripts'], { cwd: clientDir });
  console.log('[e2e] npm run build (client)');
  await run('npm', ['run', 'build'], { cwd: clientDir });

  mkdirSync(wwwroot, { recursive: true });
  await cp(clientDist, wwwroot, { recursive: true });
  console.log(`[e2e] copied ${clientDist} → ${wwwroot}`);
}

async function startDotnet() {
  console.log(`[e2e] dotnet run --project ${webProject} (port ${port}, data ${dataDir})`);
  const env = {
    ...process.env,
    ASPNETCORE_URLS: `http://127.0.0.1:${port}`,
    ASPNETCORE_ENVIRONMENT: 'Development',
    Scribegate__DataPath: dataDir,
  };

  // --no-launch-profile is required because launchSettings.json pins a fixed
  // applicationUrl that would otherwise override ASPNETCORE_URLS.
  const proc = spawn('dotnet', [
    'run',
    '--project', webProject,
    '-c', 'Release',
    '--no-build',
    '--no-restore',
    '--no-launch-profile',
  ], {
    stdio: 'inherit',
    shell: process.platform === 'win32',
    env,
  });

  const cleanup = () => {
    try {
      if (!proc.killed) proc.kill('SIGTERM');
    } catch {
      /* ignore */
    }
    try {
      rmSync(dataDir, { recursive: true, force: true });
    } catch {
      /* ignore */
    }
  };
  process.on('SIGINT', cleanup);
  process.on('SIGTERM', cleanup);
  process.on('exit', cleanup);

  proc.on('exit', (code) => process.exit(code ?? 1));
}

async function buildDotnet() {
  console.log('[e2e] dotnet build');
  await run('dotnet', ['build', webProject, '-c', 'Release', '-p:SkipClientBuild=true', '--nologo']);
}

await buildClient();
await buildDotnet();
await startDotnet();
