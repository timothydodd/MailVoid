#!/usr/bin/env node
const { spawn } = require('child_process');
const fs = require('fs');
const path = require('path');

const libOutputPath = path.join(__dirname, '../dist/rd-ui/package.json');

function runCommand(name, command, args = []) {
  console.log(`[${name}] Starting: ${command} ${args.join(' ')}`);
  const proc = spawn(command, args, {
    stdio: 'inherit',
    shell: true,
    cwd: path.join(__dirname, '..')
  });
  proc.on('error', (err) => console.error(`[${name}] Error:`, err));
  return proc;
}

function waitForFile(filePath, interval = 500, timeout = 120000) {
  return new Promise((resolve, reject) => {
    const startTime = Date.now();
    const checkFile = () => {
      if (fs.existsSync(filePath)) {
        console.log(`File found: ${filePath}`);
        resolve();
      } else if (Date.now() - startTime > timeout) {
        reject(new Error(`Timeout waiting for ${filePath}`));
      } else {
        setTimeout(checkFile, interval);
      }
    };
    checkFile();
  });
}

async function main() {
  const config = process.argv[2] || 'development';
  const processes = [];

  try {
    // Build lib first (one-time build to ensure dist exists)
    console.log('\nBuilding rd-ui library...\n');
    const libBuildProc = spawn('npm', ['run', 'lib:build'], {
      stdio: 'inherit', shell: true, cwd: path.join(__dirname, '..')
    });
    await new Promise((resolve, reject) => {
      libBuildProc.on('close', (code) => code === 0 ? resolve() : reject(new Error(`Build failed`)));
    });

    // Start lib watch - delete marker file first to detect when rebuild completes
    console.log('\nStarting rd-ui library watch mode...\n');
    if (fs.existsSync(libOutputPath)) fs.unlinkSync(libOutputPath);

    processes.push(runCommand('rd-ui', 'npm', ['run', 'lib:watch']));

    // Wait for lib:watch to complete its first build
    console.log('Waiting for library watch to complete initial build...\n');
    await waitForFile(libOutputPath);
    await new Promise(resolve => setTimeout(resolve, 1000));
    console.log('Library ready!\n');

    // Start dev server
    console.log(`Starting dev server (${config})...\n`);
    processes.push(runCommand('App', 'ng', ['serve', '--configuration', config]));

    // Cleanup handler
    const cleanup = () => {
      processes.forEach(proc => { if (!proc.killed) proc.kill('SIGTERM'); });
      process.exit(0);
    };
    process.on('SIGINT', cleanup);
    process.on('SIGTERM', cleanup);

    await Promise.all(processes.map(proc => new Promise(resolve => proc.on('close', resolve))));
  } catch (error) {
    console.error('\nError:', error.message);
    processes.forEach(proc => { if (!proc.killed) proc.kill('SIGTERM'); });
    process.exit(1);
  }
}

main().catch(error => { console.error('\nFatal error:', error); process.exit(1); });
