"use strict";
/* eslint-disable @typescript-eslint/no-require-imports, @typescript-eslint/no-unused-vars */
// 诊断脚本（临时）：被 next 子进程通过 NODE_OPTIONS --require 加载。
// 目的：区分 Pi Web 退出原因是「自身 process.exit」（shutdown/restart route）
// 还是「外部 TerminateProcess」（WindowsJobObject KILL_ON_JOB_CLOSE / 杀软）。
// 原理：process.on('exit') 只在 JS 主动 exit 或正常结束时触发；被外部
// TerminateProcess/kill 强杀时不触发。据此区分，并把栈写入 service log。
// 只追加日志，不改任何进程行为；定位完会随 pi-web.js 的 NODE_OPTIONS 一起移除。
var fs;
try { fs = require("fs"); } catch (e) {}

function diag(tag, info) {
  try {
    if (!fs) return;
    var line = "[exit-diag " + new Date().toISOString() + "] " + tag + " " + info + "\n";
    var p = process.env.PI_WEB_LOG_PATH;
    if (p) fs.appendFileSync(p, line);
  } catch (e) {}
}

try {
  diag("loaded", "pid=" + process.pid + " argv=" + JSON.stringify(process.argv.slice(1, 4)));
  process.on("exit", function (code) {
    diag("exit", "code=" + code + " stack=" + (new Error("exit-trace").stack));
  });
  process.on("beforeExit", function (code) {
    diag("beforeExit", "code=" + code + " stack=" + (new Error("beforeExit-trace").stack));
  });
  process.on("uncaughtException", function (err) {
    diag("uncaughtException", (err && err.stack) ? err.stack : String(err));
  });
  process.on("unhandledRejection", function (r) {
    diag("unhandledRejection", (r && r.stack) ? r.stack : String(r));
  });
  ["SIGINT", "SIGTERM", "SIGHUP", "SIGBREAK"].forEach(function (sig) {
    try { process.on(sig, function () { diag("signal", sig); }); } catch (e) {}
  });
} catch (e) {}
