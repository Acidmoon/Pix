import assert from "node:assert/strict";
import test from "node:test";
import { createJiti } from "jiti";

const jiti = createJiti(import.meta.url);
const {
  compareVersions,
  isNewerVersion,
  getLatestVersion,
  getUpdateStatus,
  KERNEL_PACKAGES,
  APP_PACKAGE,
} = await jiti.import("./update-check.ts");

function jsonResponse(value, status = 200) {
  return new Response(JSON.stringify(value), {
    status,
    headers: { "Content-Type": "application/json" },
  });
}

test("compareVersions orders main versions numerically", () => {
  assert.equal(compareVersions("0.80.10", "0.81.0"), -1);
  assert.equal(compareVersions("0.81.0", "0.80.10"), 1);
  assert.equal(compareVersions("1.2.3", "1.2.3"), 0);
  assert.equal(compareVersions("2.0.0", "1.9.9"), 1);
  assert.equal(compareVersions("1.2.0", "1.10.0"), -1); // 数值比较而非字典序
  assert.equal(compareVersions("v1.2.3", "1.2.3"), 0);  // 容忍 v 前缀
});

test("compareVersions treats prerelease as older than release", () => {
  assert.equal(compareVersions("1.2.3-beta.1", "1.2.3"), -1);
  assert.equal(compareVersions("1.2.3", "1.2.3-beta.1"), 1);
  assert.equal(compareVersions("1.2.3-beta.2", "1.2.3-beta.10"), -1); // 数字预发布按数值
  assert.equal(compareVersions("1.2.3-alpha", "1.2.3-beta"), -1);     // 字母预发布按字典序
});

test("isNewerVersion reflects compareVersions sign", () => {
  assert.equal(isNewerVersion("0.81.0", "0.80.10"), true);
  assert.equal(isNewerVersion("0.80.10", "0.80.10"), false);
  assert.equal(isNewerVersion("0.80.9", "0.80.10"), false);
});

test("getLatestVersion reads version and encodes scoped package url", async () => {
  const seen = [];
  const version = await getLatestVersion("@earendil-works/pi-coding-agent", {
    fetcher: async (url) => {
      seen.push(url);
      return jsonResponse({ version: "9.9.9" });
    },
  });
  assert.equal(version, "9.9.9");
  assert.match(seen[0], /registry\.npmjs\.org\/@earendil-works%2Fpi-coding-agent\/latest$/);
});

test("getLatestVersion throws on non-ok response", async () => {
  await assert.rejects(
    () => getLatestVersion("@agegr/pi-web", { fetcher: async () => jsonResponse({}, 500) }),
    /HTTP 500/,
  );
});

test("getUpdateStatus flags update when registry is newer and survives network failure", async () => {
  // 全部返回一个极高的 latest → app 与内核都应标记可更新。
  const status = await getUpdateStatus({
    force: true,
    fetcher: async () => jsonResponse({ version: "999.0.0" }),
  });
  assert.equal(status.app.pkg, APP_PACKAGE);
  assert.equal(status.app.latest, "999.0.0");
  assert.equal(status.app.updateAvailable, true);
  assert.equal(status.kernel.length, KERNEL_PACKAGES.length);
  for (const info of status.kernel) {
    assert.equal(info.latest, "999.0.0");
    assert.equal(info.updateAvailable, true);
  }
  assert.ok(status.checkedAt);

  // 网络失败：latest=null、updateAvailable=false、不抛错、记录 error。
  const failed = await getUpdateStatus({
    force: true,
    fetcher: async () => jsonResponse({}, 500),
  });
  assert.equal(failed.app.latest, null);
  assert.equal(failed.app.updateAvailable, false);
  assert.ok(failed.app.error);
  for (const info of failed.kernel) {
    assert.equal(info.latest, null);
    assert.equal(info.updateAvailable, false);
  }
});
