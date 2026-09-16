#!/usr/bin/env node
/*
 * CodexRouterSwitch 模型管理桥（Model Panel Bridge）
 *
 * 供 CodexRouterSwitch.exe 调用的只读/受控写入桥接脚本。
 * 完全复用 codex-router 安装目录内的官方模块与目录文件，不修改其任何文件：
 *   - 读取:   merged-models.json / native-models.json / model-picker.json
 *   - 写入:   model-picker.json（经官方跨进程锁 + 一次官方 catalog 发布）
 *   - 发现:   src/model-discovery.mjs（联网拉取供应商 /models 端点）
 *   - 加入:   src/curate-models.mjs --models ... --apply（官方 curation 路线）
 *
 * 命令（全部输出单行 JSON 到 stdout，错误同样以 JSON 呈现并以非零码退出）：
 *   list                        输出 供应商 → 模型公司 → 模型 三层数据
 *   apply  --input <json文件>   批量应用显示/隐藏（{"show":[...],"hide":[...]}）
 *   discover [--refresh]        联网发现各已配置供应商的新模型
 *   add    --input <json文件>   加入新模型（{"provider":"...","models":[...]}）
 *
 * 环境变量（与切换器一致，均有默认推导）：
 *   CODEX_ROUTER_SWITCH_ROUTER_ROOT / CODEX_ROUTER_SWITCH_CODEX_HOME
 *   CODEX_ROUTER_SWITCH_BACKUP_DIR（默认 %LOCALAPPDATA%\CodexRouterSwitch\backups）
 */

import { copyFileSync, existsSync, mkdirSync, readFileSync, writeFileSync } from "node:fs";
import os from "node:os";
import path from "node:path";
import { spawnSync } from "node:child_process";
import { fileURLToPath, pathToFileURL } from "node:url";

// ---------------------------------------------------------------------------
// 环境与路径
// ---------------------------------------------------------------------------

const localAppData =
  process.env.LOCALAPPDATA || path.join(os.homedir(), "AppData", "Local");

const RouterRoot = path.resolve(
  process.env.CODEX_ROUTER_SWITCH_ROUTER_ROOT ||
    path.join(localAppData, "codex-router"),
);
const CodexHome = path.resolve(
  process.env.CODEX_ROUTER_SWITCH_CODEX_HOME ||
    path.join(os.homedir(), ".codex"),
);
const StateDir = path.join(CodexHome, "codex-router");

// 让 codex-router 的模块解析到与切换器一致的目录（未显式设置时才填默认值）。
process.env.MODEL_ROUTER_TARGET ||= "codex";
process.env.CODEX_HOME ||= CodexHome;
process.env.MODEL_ROUTER_STATE_DIR ||= StateDir;
process.env.CODEX_ROUTER_STATE_DIR ||= StateDir;

const BackupRoot = path.resolve(
  process.env.CODEX_ROUTER_SWITCH_BACKUP_DIR ||
    path.join(localAppData, "CodexRouterSwitch", "backups"),
);

const srcUrl = (name) => pathToFileURL(path.join(RouterRoot, "src", name)).href;
const srcPath = (name) => path.join(RouterRoot, "src", name);

function timestamp() {
  const d = new Date();
  const pad = (n) => String(n).padStart(2, "0");
  return (
    `${d.getFullYear()}${pad(d.getMonth() + 1)}${pad(d.getDate())}-` +
    `${pad(d.getHours())}${pad(d.getMinutes())}${pad(d.getSeconds())}`
  );
}

function readJsonIfExists(file, fallback = null) {
  try {
    if (!existsSync(file)) return fallback;
    return JSON.parse(readFileSync(file, "utf8"));
  } catch {
    return fallback;
  }
}

function output(payload, exitCode = 0) {
  process.stdout.write(`${JSON.stringify(payload)}\n`);
  process.exit(exitCode);
}

// ---------------------------------------------------------------------------
// 公司（模型厂商）识别
// ---------------------------------------------------------------------------

const COMPANY_NAMES = new Map(
  Object.entries({
    deepseek: "DeepSeek",
    qwen: "Qwen",
    google: "Google",
    meta: "Meta",
    openai: "OpenAI",
    anthropic: "Anthropic",
    "zai-org": "Z.AI",
    "z-ai": "Z.AI",
    glm: "Z.AI",
    tencent: "Tencent",
    hunyuan: "Tencent",
    moonshot: "Moonshot",
    moonshotai: "Moonshot",
    xai: "xAI",
    "x-ai": "xAI",
    minimax: "MiniMax",
    minimaxai: "MiniMax",
    nvidia: "NVIDIA",
    xiaomi: "Xiaomi",
    stepfun: "StepFun",
    thinkingmachines: "Thinking Machines",
    "thinking-machines": "Thinking Machines",
    sakana: "Sakana AI",
    poolside: "Poolside",
    microsoft: "Microsoft",
    baidu: "Baidu",
    bytedance: "ByteDance",
    inclusionai: "InclusionAI",
    longcat: "LongCat",
    nousresearch: "Nous Research",
    "nous-research": "Nous Research",
    zhipu: "Z.AI",
    liquid: "Liquid AI",
    liquidai: "Liquid AI",
    arcee: "Arcee AI",
    cohere: "Cohere",
    mistral: "Mistral",
    mistralai: "Mistral",
  }),
);

// 无法从上游命名空间前缀识别时的模型名启发（顺序匹配）。
const NAME_HINTS = [
  [/^claude/i, "Anthropic"],
  [/^gemini/i, "Google"],
  [/^gpt[-.]?/i, "OpenAI"],
  [/^grok/i, "xAI"],
  [/^kimi/i, "Moonshot"],
  [/^k3\b/i, "Moonshot"],
  [/^glm/i, "Z.AI"],
  [/^minimax/i, "MiniMax"],
  [/^deepseek/i, "DeepSeek"],
  [/^qwen/i, "Qwen"],
  [/^hunyuan|^hy\d/i, "Tencent"],
  [/^mimo/i, "Xiaomi"],
  [/^step[-.]/i, "StepFun"],
  [/^nemotron/i, "NVIDIA"],
  [/^inkling/i, "Thinking Machines"],
  [/^fugu/i, "Sakana AI"],
  [/^laguna/i, "Poolside"],
  [/^mistral|^magistral/i, "Mistral"],
  [/^llama/i, "Meta"],
];

function companyFromUpstream(upstreamModel) {
  const value = String(upstreamModel || "").trim();
  if (!value) return null;
  const slash = value.indexOf("/");
  if (slash > 0) {
    const prefix = value.slice(0, slash).trim();
    if (prefix) return prefix;
  }
  return null;
}

function normalizeCompanyKey(key) {
  return String(key || "").trim().toLowerCase();
}

function companyDisplayName(key) {
  const mapped = COMPANY_NAMES.get(normalizeCompanyKey(key));
  if (mapped) return mapped;
  const raw = String(key || "").trim();
  if (!raw) return "其他";
  // 未知前缀保留原文，仅做首字母大写，避免凭空编造厂商名。
  return raw.charAt(0).toUpperCase() + raw.slice(1);
}

function resolveCompany(meta, mergedEntry) {
  const upstream = meta?.upstreamModel || "";
  const fromUpstream = companyFromUpstream(upstream);
  if (fromUpstream) return companyDisplayName(fromUpstream);

  // claude-fable-5-1 这类无命名空间的上游 id，用模型名启发。
  const candidate = String(upstream || meta?.slug || mergedEntry?.slug || "");
  for (const [pattern, name] of NAME_HINTS) {
    if (pattern.test(candidate)) return name;
  }

  const displayName = String(
    meta?.displayName || mergedEntry?.display_name || "",
  );
  for (const [pattern, name] of NAME_HINTS) {
    if (pattern.test(displayName)) return name;
  }

  return "其他";
}

// ---------------------------------------------------------------------------
// 共享数据装载
// ---------------------------------------------------------------------------

async function loadContext() {
  const pathsMod = await import(srcUrl("paths.mjs"));
  const registry = await import(srcUrl("model-registry.mjs"));
  const pickerState = await import(srcUrl("model-picker-state.mjs"));

  const merged = readJsonIfExists(pathsMod.MERGED_CATALOG_PATH, null);
  const native = readJsonIfExists(pathsMod.NATIVE_CATALOG_PATH, null);

  const visible = pickerState.readVisibleModels();
  const hidden = pickerState.readHiddenModels();

  const nativeBaseSlugs = new Set(
    (Array.isArray(native?.models) ? native.models : [])
      .map((model) => String(model?.slug || ""))
      .filter(Boolean),
  );

  return {
    pathsMod,
    registry,
    pickerState,
    merged,
    native,
    visible,
    hidden,
    nativeBaseSlugs,
  };
}

function readCodexMode() {
  try {
    const configPath = path.join(CodexHome, "config.toml");
    if (!existsSync(configPath)) return "unknown";
    const contents = readFileSync(configPath, "utf8");
    return /^# BEGIN (?:kimi-)?codex-(?:router|proxy)-/m.test(contents)
      ? "router"
      : "native";
  } catch {
    return "unknown";
  }
}

// ---------------------------------------------------------------------------
// list：三层数据
// ---------------------------------------------------------------------------

async function cmdList() {
  const ctx = await loadContext();
  const { registry, merged, visible, nativeBaseSlugs } = ctx;
  const warnings = [];

  // catalog 里记录的最终可见性（"list" / "hide"）——对 native 基础模型这是
  // 权威值；routed 模型的该字段本就由 picker 状态推导，两者一致。
  const mergedVisibility = new Map();
  for (const model of merged?.models || []) {
    if (model?.slug) {
      mergedVisibility.set(String(model.slug), String(model.visibility || ""));
    }
  }

  const entries = [];
  const seen = new Set();

  const pushEntry = (slug, displayName, providerId, upstreamModel, source) => {
    if (!slug || seen.has(slug)) return;
    seen.add(slug);
    entries.push({ slug, displayName, providerId, upstreamModel, source });
  };

  // 1) 已发布目录（Codex 当前可见的模型全集）
  for (const model of merged?.models || []) {
    const slug = String(model?.slug || "");
    if (!slug) continue;
    const meta = registry.MODEL_BY_SLUG.get(slug) || null;
    const routed = slug.includes("/");
    const providerId = routed
      ? meta?.provider || slug.split("/")[0]
      : "openai-native";
    pushEntry(
      slug,
      String(model?.display_name || meta?.displayName || slug),
      providerId,
      meta?.upstreamModel || "",
      "catalog",
    );
  }

  // 2) 已选供应商中「已加入但尚未发布」的模型（curate 之后、应用之前）
  try {
    const providerSelection = await import(srcUrl("provider-selection.mjs"));
    for (const model of providerSelection.selectedConfiguredListedModels()) {
      const slug = String(model?.slug || "");
      if (!slug || seen.has(slug)) continue;
      pushEntry(
        slug,
        String(model?.displayName || slug),
        String(model?.provider || slug.split("/")[0]),
        model?.upstreamModel || "",
        "pending",
      );
    }
  } catch (error) {
    warnings.push(`读取已选供应商失败：${error.message}`);
  }

  if (!merged) {
    warnings.push(
      "尚未生成模型目录（merged-models.json 不存在）。请先运行一次路由切换，或点击「应用」发布。",
    );
  }

  // 3) 分组装配
  const providers = new Map();
  for (const entry of entries) {
    const routed = entry.providerId !== "openai-native";
    const provider = providers.get(entry.providerId) || {
      id: entry.providerId,
      name: routed
        ? registry.RUNTIME_PROVIDERS.get(entry.providerId)?.displayName ||
          entry.providerId
        : "Codex 原生",
      companies: new Map(),
    };
    providers.set(entry.providerId, provider);

    const companyName = routed
      ? resolveCompany(
          registry.MODEL_BY_SLUG.get(entry.slug) || null,
          { slug: entry.slug },
        )
      : "OpenAI";
    const company = provider.companies.get(companyName) || {
      id: companyName,
      name: companyName,
      models: [],
    };
    provider.companies.set(companyName, company);

    const isNativeBase = nativeBaseSlugs.has(entry.slug);
    const catalogVisibility = mergedVisibility.get(entry.slug);
    const isVisible =
      catalogVisibility === "list"
        ? true
        : catalogVisibility === "hide"
          ? false
          : visible.has(entry.slug);
    company.models.push({
      slug: entry.slug,
      name: entry.displayName,
      visible: isVisible,
      toggleable: !isNativeBase,
      note: isNativeBase
        ? "由 Codex 管理"
        : entry.source === "pending"
          ? "尚未发布，应用后生效"
          : null,
    });
  }

  const providerList = [...providers.values()]
    .map((provider) => {
      const companies = [...provider.companies.values()]
        .map((company) => {
          company.models.sort((a, b) => a.name.localeCompare(b.name, "zh"));
          return {
            id: company.id,
            name: company.name,
            total: company.models.length,
            visible: company.models.filter((m) => m.visible).length,
            models: company.models,
          };
        })
        .sort((a, b) => a.name.localeCompare(b.name, "en"));
      return {
        id: provider.id,
        name: provider.name,
        total: companies.reduce((sum, c) => sum + c.total, 0),
        visible: companies.reduce((sum, c) => sum + c.visible, 0),
        companies,
      };
    })
    .sort((a, b) => {
      // Codex 原生始终排在最后，其余按名称排序。
      if (a.id === "openai-native") return 1;
      if (b.id === "openai-native") return -1;
      return a.name.localeCompare(b.name, "en");
    });

  const stats = {
    total: providerList.reduce((sum, p) => sum + p.total, 0),
    visible: providerList.reduce((sum, p) => sum + p.visible, 0),
  };
  stats.hidden = stats.total - stats.visible;

  output({
    ok: true,
    command: "list",
    routerRoot: RouterRoot,
    stateDir: StateDir,
    codexMode: readCodexMode(),
    generatedAt: new Date().toISOString(),
    stats,
    providers: providerList,
    warnings,
  });
}

// ---------------------------------------------------------------------------
// 备份
// ---------------------------------------------------------------------------

function backupFiles(files) {
  for (const file of files) {
    if (typeof file !== "string" || !file) {
      throw new Error("内部错误：备份路径解析失败，已中止以避免丢失备份。");
    }
  }
  const existing = files.filter((file) => existsSync(file));
  if (existing.length === 0) return null;
  const dir = path.join(BackupRoot, timestamp());
  mkdirSync(dir, { recursive: true });
  for (const file of existing) {
    copyFileSync(file, path.join(dir, path.basename(file)));
  }
  return dir;
}

// ---------------------------------------------------------------------------
// apply：批量显示/隐藏
// ---------------------------------------------------------------------------

async function cmdApply(inputFile) {
  if (!inputFile) throw new Error("缺少 --input 参数。");
  const payload = JSON.parse(readFileSync(inputFile, "utf8"));
  const show = [...new Set((payload.show || []).map(String).filter(Boolean))];
  const hide = [...new Set((payload.hide || []).map(String).filter(Boolean))];

  const ctx = await loadContext();
  const { registry, merged, pickerState, nativeBaseSlugs, pathsMod } = ctx;

  const known = new Set();
  for (const model of merged?.models || []) known.add(String(model?.slug || ""));
  const providerSelection = await import(srcUrl("provider-selection.mjs"));
  for (const model of providerSelection.selectedConfiguredListedModels()) {
    known.add(String(model?.slug || ""));
  }

  const warnings = [];
  const acceptedShow = show.filter((slug) => {
    if (nativeBaseSlugs.has(slug)) {
      warnings.push(`${slug} 由 Codex 管理，已忽略。`);
      return false;
    }
    if (!known.has(slug)) {
      warnings.push(`未知模型已忽略：${slug}`);
      return false;
    }
    return true;
  });
  const acceptedHide = hide.filter((slug) => {
    if (nativeBaseSlugs.has(slug)) {
      warnings.push(`${slug} 由 Codex 管理，已忽略。`);
      return false;
    }
    if (!known.has(slug)) {
      warnings.push(`未知模型已忽略：${slug}`);
      return false;
    }
    return true;
  });

  const backupDir = backupFiles([pickerState.MODEL_PICKER_STATE_PATH]);

  const { withModelOverlayLock } = await import(srcUrl("model-overlay-lock.mjs"));
  await withModelOverlayLock(async () => {
    if (acceptedShow.length) pickerState.setModelsVisible(acceptedShow, true);
    if (acceptedHide.length) pickerState.setModelsVisible(acceptedHide, false);

    // 官方锁序：持有 overlay 锁时，由全新子进程执行 catalog 发布。
    const result = spawnSync(
      process.execPath,
      [srcPath("catalog.mjs")],
      {
        cwd: RouterRoot,
        env: process.env,
        encoding: "utf8",
        timeout: 240_000,
        windowsHide: true,
      },
    );
    if (result.error) throw result.error;
    if (result.status !== 0) {
      const detail = String(result.stderr || result.stdout || "").trim();
      throw new Error(
        `模型目录发布失败（切换器已保存可见性，可稍后重试应用）：${detail.slice(0, 600)}`,
      );
    }
  });

  const publishedVisible = ctx.pickerState.readVisibleModels();
  output({
    ok: true,
    command: "apply",
    shown: acceptedShow.length,
    hidden: acceptedHide.length,
    published: true,
    visibleTotal: publishedVisible.size,
    backupDir,
    codexMode: readCodexMode(),
    warnings,
  });
}

// ---------------------------------------------------------------------------
// discover：联网发现（自实现，与 router 转发相同的目标端点）
// ---------------------------------------------------------------------------
//
// codex-router 自带的发现路径会在 DNS 解析结果包含保留段地址时按 SSRF 防护
// 拒绝（本机代理软件的 fake-ip DNS 把公网域名解析为 198.18.x.x，恰好命中该
// 规则），导致官方 discover 在此网络环境下不可用。这里直接对注册表中已定义
// 的官方 baseUrl 发起同一请求（带供应商凭据），只拉取模型列表用于对比。

function friendlyDiscoveryError(error) {
  const message = error instanceof Error ? error.message : String(error);
  if (/abort|timeout/i.test(message)) return "连接超时，请检查网络。";
  if (/ENOTFOUND|EAI_AGAIN|getaddrinfo/i.test(message)) return "无法解析供应商域名。";
  if (/ECONNREFUSED|ECONNRESET|socket/i.test(message)) return "连接被拒绝或中断。";
  return message;
}

async function cmdDiscover() {
  const providerSelection = await import(srcUrl("provider-selection.mjs"));
  const registry = await import(srcUrl("model-registry.mjs"));
  const credentials = await import(srcUrl("provider-credentials.mjs"));

  const selected = providerSelection.readProviderSelection();
  const configured = new Set(providerSelection.configuredProviderIds());
  const requested = [
    ...new Set(selected.map((id) => providerSelection.canonicalProviderId(id))),
  ].filter((id) => configured.has(id));

  const skipLocal = new Set(["local", "lmstudio", "ollama", "custom"]);
  const results = [];

  for (const providerId of requested) {
    const provider = registry.RUNTIME_PROVIDERS.get(providerId);
    const displayName = provider?.displayName || providerId;
    if (!provider || skipLocal.has(providerId)) continue;

    if (provider.kind !== "openai-compatible") {
      results.push({
        id: providerId,
        name: displayName,
        error: "该供应商不使用模型列表接口（OAuth 登录）。",
        fetchedAt: null,
        discovered: 0,
        registered: 0,
        addable: [],
        unavailable: [],
      });
      continue;
    }

    // 家族集合：父供应商 + 其协议变体（它们的 /models 端点相同）。
    const family = new Set([providerId]);
    for (const [rid, rp] of registry.RUNTIME_PROVIDERS) {
      if (rp?.variantOf === providerId) family.add(rid);
    }
    const registeredIds = [
      ...new Set(
        registry.LISTED_MODELS.filter((model) => family.has(model.provider))
          .map((model) => String(model.upstreamModel || ""))
          .filter(Boolean),
      ),
    ];

    try {
      const { baseUrl } = registry.resolveProviderBaseUrl(provider);
      const credential = credentials.resolveProviderCredential(provider);
      const headers = { accept: "application/json" };
      if (credential?.value) headers.authorization = `Bearer ${credential.value}`;

      const response = await fetch(
        `${String(baseUrl).replace(/\/+$/, "")}/models`,
        { headers, signal: AbortSignal.timeout(30_000) },
      );
      if (!response.ok) {
        results.push({
          id: providerId,
          name: displayName,
          error: `供应商返回 HTTP ${response.status}。`,
          fetchedAt: null,
          discovered: 0,
          registered: registeredIds.length,
          addable: [],
          unavailable: [],
        });
        continue;
      }

      const payload = await response.json();
      const list = Array.isArray(payload)
        ? payload
        : payload?.data ?? payload?.models ?? [];
      const discovered = (Array.isArray(list) ? list : [])
        .map((item) => ({
          id: String(item?.id || "").trim(),
          name: item?.name ? String(item.name) : null,
          contextWindow:
            Number.isInteger(item?.context_length)
              ? item.context_length
              : Number.isInteger(item?.contextLength)
                ? item.contextLength
                : null,
        }))
        .filter((item) => item.id);

      const discoveredIds = new Set(discovered.map((item) => item.id));
      const registeredSet = new Set(registeredIds);

      // 协议安全门：上游对"尚未验证线路协议"的候选模型 fail-closed（防止
      // picker 出现必然失败的条目）。能加入的候选与官方 discover 保持同一判据。
      const curation = await import(srcUrl("opencode-curation.mjs"));
      const addable = [];
      const blocked = [];
      for (const item of discovered) {
        if (registeredSet.has(item.id)) continue;
        const reason = curation.curatedModelBlockReason(providerId, item.id);
        if (reason) blocked.push({ id: item.id, reason });
        else addable.push(item);
      }

      results.push({
        id: providerId,
        name: displayName,
        error: null,
        fetchedAt: new Date().toISOString(),
        discovered: discovered.length,
        registered: registeredIds.length,
        addable: addable.slice(0, 300),
        addableTotal: addable.length,
        blockedTotal: blocked.length,
        blocked: blocked.slice(0, 20),
        unavailable: registeredIds.filter((id) => !discoveredIds.has(id)),
      });
    } catch (error) {
      results.push({
        id: providerId,
        name: displayName,
        error: friendlyDiscoveryError(error),
        fetchedAt: null,
        discovered: 0,
        registered: registeredIds.length,
        addable: [],
        unavailable: [],
      });
    }
  }

  output({
    ok: true,
    command: "discover",
    live: true,
    checkedAt: new Date().toISOString(),
    providers: results,
    warnings: requested.length === 0 ? ["没有可联网检查的供应商。"] : [],
  });
}

// ---------------------------------------------------------------------------
// add：加入新模型（官方数据格式与发布管线，落库由本桥执行）
// ---------------------------------------------------------------------------
//
// 上游 curate-models 的发现步骤同样会被 fake-ip 环境的 SSRF 防护拒绝，因此这里
// 复刻其确定性 --models 路径：官方 userModelEntry 构造条目、官方协议安全门校验、
// 官方 overlay 事务（锁内写入 + 网关路由重建 + 客户端重发布）。

function normalizedAddItems(payload) {
  return (Array.isArray(payload.models) ? payload.models : [])
    .map((item) =>
      typeof item === "string"
        ? { id: String(item).trim(), name: null, contextWindow: null }
        : {
            id: String(item?.id || "").trim(),
            name: item?.name ? String(item.name) : null,
            contextWindow: Number.isInteger(item?.contextWindow)
              ? item.contextWindow
              : null,
          },
    )
    .filter((item) => item.id);
}

// 与上游 curate-models.mjs 的 curatedSizing 同一换算：Codex 在声明窗口的
// 85% 处触发自动压缩。上游模块顶层含可执行校验、无法安全 import，故在此复刻。
const AUTO_COMPACT_RATIO = 0.85;
function curatedSizing(contextLength) {
  if (!Number.isInteger(contextLength) || contextLength < 1) return undefined;
  return {
    contextWindow: contextLength,
    autoCompact: Math.floor(contextLength * AUTO_COMPACT_RATIO),
  };
}

async function cmdAdd(inputFile) {
  if (!inputFile) throw new Error("缺少 --input 参数。");
  const payload = JSON.parse(readFileSync(inputFile, "utf8"));
  const providerInput = String(payload.provider || "").trim();
  const items = [...new Map(
    normalizedAddItems(payload).map((item) => [item.id, item]),
  ).values()];
  if (!providerInput) throw new Error("缺少 provider。");
  if (items.length === 0) throw new Error("没有要加入的模型。");

  const providerSelection = await import(srcUrl("provider-selection.mjs"));
  const registry = await import(srcUrl("model-registry.mjs"));
  const curation = await import(srcUrl("opencode-curation.mjs"));
  const userModels = await import(srcUrl("user-models.mjs"));
  const pickerState = await import(srcUrl("model-picker-state.mjs"));
  const overlay = await import(srcUrl("model-overlay-publication.mjs"));

  const canonical = providerSelection.canonicalProviderId(providerInput);
  const provider = registry.RUNTIME_PROVIDERS.get(canonical);
  if (!provider) throw new Error(`未知供应商：${providerInput}`);
  if (provider.kind !== "openai-compatible") {
    throw new Error("该供应商不支持按模型加入（OAuth 登录类没有可加入的目录）。");
  }
  if (!new Set(providerSelection.configuredProviderIds()).has(canonical)) {
    throw new Error(`供应商尚未配置凭据：${canonical}`);
  }

  const skipped = [];
  const accepted = [];
  for (const item of items) {
    const reason = curation.curatedModelBlockReason(canonical, item.id);
    if (reason) skipped.push({ id: item.id, reason });
    else accepted.push(item);
  }

  const existing = userModels.readUserModels();
  const existingKeys = new Set(
    existing.map((model) => `${model.provider}|${model.upstreamModel}`),
  );
  const mineCount = existing.filter(
    (model) =>
      providerSelection.canonicalProviderId(String(model.provider || "")) ===
      canonical,
  ).length;

  const entries = [];
  let priority = 100 + mineCount;
  for (const item of accepted) {
    const routedProviderId = curation.curatedModelProviderId(canonical, item.id);
    if (existingKeys.has(`${routedProviderId}|${item.id}`)) {
      skipped.push({ id: item.id, reason: "已加入，跳过。" });
      continue;
    }
    const sizing =
      curatedSizing(item.contextWindow) ||
      curatedSizing(curation.curatedModelContextLength(canonical, item.id));
    const metadata = {};
    if (sizing) Object.assign(metadata, sizing);
    if (item.name) metadata.displayName = item.name;
    entries.push(
      userModels.userModelEntry({
        providerId: routedProviderId,
        upstreamId: item.id,
        requestProfile: undefined,
        priority: priority,
        metadata: Object.keys(metadata).length ? metadata : undefined,
      }),
    );
    priority += 1;
  }

  if (entries.length === 0) {
    output({
      ok: true,
      command: "add",
      provider: canonical,
      added: 0,
      savedSlugs: [],
      skipped,
      backupDir: null,
      codexMode: readCodexMode(),
      note:
        skipped.length && skipped.every((item) => !/已加入/.test(item.reason))
          ? "候选模型均被当前路由器版本的协议验证限制拦下，需等待上游版本更新后加入。"
          : null,
      warnings: [],
    });
  }

  const backupDir = backupFiles([
    userModels.USER_MODELS_PATH,
    pickerState.MODEL_PICKER_STATE_PATH,
  ]);

  await overlay.transactModelOverlayMutation({
    files: [userModels.USER_MODELS_PATH, pickerState.MODEL_PICKER_STATE_PATH],
    mutate: () => {
      // 锁内重读，防止并发 curation 期间基于陈旧列表覆盖他人条目。
      const current = userModels.readUserModels();
      const currentKeys = new Set(
        current.map((model) => `${model.provider}|${model.upstreamModel}`),
      );
      const fresh = entries.filter(
        (entry) => !currentKeys.has(`${entry.provider}|${entry.upstreamModel}`),
      );
      if (fresh.length === 0) return;
      userModels.writeUserModels([...current, ...fresh]);
      const slugs = fresh
        .map((entry) => String(entry.slug || ""))
        .filter(Boolean);
      if (slugs.length) pickerState.setModelsVisible(slugs, true);
    },
    restart: false,
  });

  const savedSlugs = entries
    .map((entry) => String(entry.slug || ""))
    .filter(Boolean);

  output({
    ok: true,
    command: "add",
    provider: canonical,
    added: savedSlugs.length,
    savedSlugs,
    skipped,
    backupDir,
    codexMode: readCodexMode(),
    warnings: [],
  });
}

// ---------------------------------------------------------------------------
// 入口
// ---------------------------------------------------------------------------

function optionValue(name) {
  const index = process.argv.indexOf(name);
  return index === -1 ? undefined : process.argv[index + 1];
}

async function main() {
  const command = process.argv[2];
  if (!command || command === "--help" || command === "-h") {
    process.stdout.write(
      "Usage: model-panel.mjs list | apply --input FILE | discover [--refresh] | add --input FILE\n",
    );
    process.exit(0);
  }

  if (!existsSync(path.join(RouterRoot, "src", "catalog.mjs"))) {
    throw new Error(`未找到 codex-router 安装：${RouterRoot}`);
  }

  switch (command) {
    case "list":
      await cmdList();
      break;
    case "apply":
      await cmdApply(optionValue("--input"));
      break;
    case "discover":
      await cmdDiscover();
      break;
    case "add":
      await cmdAdd(optionValue("--input"));
      break;
    default:
      throw new Error(`未知命令：${command}`);
  }
}

main().catch((error) => {
  output(
    {
      ok: false,
      error: error instanceof Error ? error.message : String(error),
    },
    1,
  );
});
