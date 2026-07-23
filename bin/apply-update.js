#!/usr/bin/env node
"use strict";

/**
 * 整应用升级的「换文件」步骤，由 C# 启动器在服务退出后、重启前调用：
 *
 *   node bin/apply-update.js <sourceDir> [--no-cleanup]
 *
 * - <sourceDir>：新版应用目录（npm pack 解包的 package/），或回滚时的备份目录。
 * - 把 bin/.next/public/next.config.ts/package.json 覆盖进 web root（= cwd），
 *   保留 node_modules/logs/.env 与用户数据。
 * - 跑 npm install --omit=dev 让 node_modules 对齐新 package.json。
 * - 默认成功后删除 <sourceDir>（暂存目录）；--no-cleanup 时保留（回滚用的备份）。
 *
 * 退出码 0 = 成功；非 0 = 失败（启动器据此从备份回滚）。
 */

// eslint-disable-next-line @typescript-eslint/no-require-imports
const { spawnSync } = require("child_process");
// eslint-disable-next-line @typescript-eslint/no-require-imports
const path = require("path");
// eslint-disable-next-line @typescript-eslint/no-require-imports
const fs = require("fs");

const args = process.argv.slice(2);
const noCleanup = args.includes("--no-cleanup");
const sourceDir = args.find((a) => !a.startsWith("--"));

if (!sourceDir) {
  console.error("Usage: apply-update.js <sourceDir> [--no-cleanup]");
  process.exit(2);
}

const webRoot = process.cwd();
const SWAP_ENTRIES = ["bin", ".next", "public", "next.config.ts", "package.json"];

function log(message) {
  console.log(`[apply-update] ${message}`);
}

function findNpmCli() {
  const nodeDir = path.dirname(process.execPath);
  const candidates = [
    path.join(nodeDir, "node_modules", "npm", "bin", "npm-cli.js"),
    path.join(nodeDir, "..", "lib", "node_modules", "npm", "bin", "npm-cli.js"),
  ];
  return candidates.find((p) => fs.existsSync(p)) ?? null;
}

function runNpm(npmArgs) {
  const npmCli = findNpmCli();
  const result = npmCli
    ? spawnSync(process.execPath, [npmCli, ...npmArgs], { cwd: webRoot, stdio: "inherit" })
    : spawnSync("npm", npmArgs, { cwd: webRoot, stdio: "inherit", shell: true });
  return result.status ?? 1;
}

try {
  if (!fs.existsSync(sourceDir)) {
    console.error(`[apply-update] sourceDir not found: ${sourceDir}`);
    process.exit(1);
  }

  for (const entry of SWAP_ENTRIES) {
    const src = path.join(sourceDir, entry);
    const dest = path.join(webRoot, entry);
    if (!fs.existsSync(src)) {
      log(`skip (not in source): ${entry}`);
      continue;
    }
    log(`replace: ${entry}`);
    fs.rmSync(dest, { recursive: true, force: true });
    fs.cpSync(src, dest, { recursive: true });
  }

  log("syncing dependencies (npm install --omit=dev)…");
  const code = runNpm(["install", "--omit=dev", "--no-audit", "--no-fund", "--loglevel=warn"]);
  if (code !== 0) {
    console.error(`[apply-update] npm install exited with ${code}`);
    process.exit(1);
  }

  if (!noCleanup) {
    log(`cleanup: ${sourceDir}`);
    fs.rmSync(sourceDir, { recursive: true, force: true });
  }

  log("done");
  process.exit(0);
} catch (error) {
  console.error(`[apply-update] failed: ${error && error.stack ? error.stack : error}`);
  process.exit(1);
}
