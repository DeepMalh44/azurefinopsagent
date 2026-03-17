<template>
  <div class="chat-view">
    <!-- Left sidebar -->
    <aside class="sidebar">
      <div class="sidebar-scroll">
        <!-- Agents (hidden for v1 — re-enable by setting showAgents to true) -->
        <div v-if="showAgents" class="sidebar-category">
          <div class="sidebar-category-label">Agents</div>
          <div class="sidebar-agent">
            <span class="sidebar-agent-icon sidebar-agent-icon--claude">
              <img
                src="/icons/claude.png"
                width="18"
                height="18"
                alt="Claude"
              />
            </span>
            <span>Anthropic Claude</span>
          </div>
          <div class="sidebar-agent">
            <span class="sidebar-agent-icon sidebar-agent-icon--codex">
              <img src="/icons/codex.png" width="18" height="18" alt="Codex" />
            </span>
            <span>OpenAI Codex</span>
          </div>
        </div>

        <!-- Suggested questions -->
        <div class="sidebar-category sidebar-questions">
          <div
            class="sidebar-category-label sidebar-category-label--toggle"
            @click="toggleSection('prompts')"
          >
            <span>Prompts</span>
            <svg
              class="collapse-chevron"
              :class="{
                'collapse-chevron--collapsed': collapsedSections.prompts,
              }"
              viewBox="0 0 16 16"
              fill="none"
            >
              <path
                d="M4 6l4 4 4-4"
                stroke="currentColor"
                stroke-width="1.5"
                stroke-linecap="round"
                stroke-linejoin="round"
              />
            </svg>
          </div>
          <div
            class="collapse-body"
            :class="{ 'collapse-body--collapsed': collapsedSections.prompts }"
          >
            <button
              v-for="q in suggestedQuestions"
              :key="q.prompt"
              class="sidebar-question"
              :disabled="streaming"
              :title="q.prompt"
              @click="sendQuestion(q.prompt)"
            >
              <span class="sidebar-question-icon">AI</span>
              <span>{{ q.label }}</span>
            </button>
          </div>
        </div>

        <!-- Connected sources -->
        <div class="sidebar-category sidebar-sources">
          <div
            class="sidebar-category-label sidebar-category-label--toggle"
            @click="toggleSection('connected')"
          >
            <span>Connected</span>
            <svg
              class="collapse-chevron"
              :class="{
                'collapse-chevron--collapsed': collapsedSections.connected,
              }"
              viewBox="0 0 16 16"
              fill="none"
            >
              <path
                d="M4 6l4 4 4-4"
                stroke="currentColor"
                stroke-width="1.5"
                stroke-linecap="round"
                stroke-linejoin="round"
              />
            </svg>
          </div>
          <div
            class="collapse-body"
            :class="{ 'collapse-body--collapsed': collapsedSections.connected }"
          >
            <div
              v-for="s in connectedSources"
              :key="s.name"
              class="sidebar-source"
            >
              <span class="sidebar-source-dot"></span>
              <span>{{ s.name }}</span>
            </div>
          </div>
        </div>

        <!-- Tool calls -->
        <div class="sidebar-category sidebar-tools" v-if="allToolCalls.length">
          <div
            class="sidebar-category-label sidebar-category-label--toggle"
            @click="toggleSection('tools')"
          >
            <span>Tools</span>
            <span style="display: flex; align-items: center; gap: 4px">
              <span class="st-count">{{ allToolCalls.length }}</span>
              <svg
                class="collapse-chevron"
                :class="{
                  'collapse-chevron--collapsed': collapsedSections.tools,
                }"
                viewBox="0 0 16 16"
                fill="none"
              >
                <path
                  d="M4 6l4 4 4-4"
                  stroke="currentColor"
                  stroke-width="1.5"
                  stroke-linecap="round"
                  stroke-linejoin="round"
                />
              </svg>
            </span>
          </div>

          <div
            class="st-list collapse-body"
            :class="{ 'collapse-body--collapsed': collapsedSections.tools }"
          >
            <div
              v-for="tc in reversedToolCalls"
              :key="tc._uid"
              :class="[
                'st-row',
                { 'st-row--running': !tc.done, 'st-row--clickable': tc.done },
              ]"
              @click.stop="
                tc.done && (hoveredTool = hoveredTool === tc ? null : tc)
              "
            >
              <!-- Running spinner -->
              <svg
                v-if="!tc.done"
                class="st-icon st-icon--spin"
                viewBox="0 0 16 16"
                fill="none"
              >
                <circle
                  cx="8"
                  cy="8"
                  r="6"
                  stroke="#bf8700"
                  stroke-width="2"
                  stroke-dasharray="28"
                  stroke-dashoffset="8"
                  stroke-linecap="round"
                />
              </svg>
              <!-- Success checkmark -->
              <svg
                v-else-if="tc.success"
                class="st-icon st-icon--ok"
                viewBox="0 0 16 16"
                fill="none"
              >
                <circle
                  cx="8"
                  cy="8"
                  r="7"
                  stroke="#1a7f37"
                  stroke-width="1.5"
                />
                <path
                  d="M5 8.2 7 10.2 11 6"
                  stroke="#1a7f37"
                  stroke-width="1.5"
                  stroke-linecap="round"
                  stroke-linejoin="round"
                />
              </svg>
              <!-- Failure X -->
              <svg
                v-else
                class="st-icon st-icon--fail"
                viewBox="0 0 16 16"
                fill="none"
              >
                <circle
                  cx="8"
                  cy="8"
                  r="7"
                  stroke="#cf222e"
                  stroke-width="1.5"
                />
                <path
                  d="M5.5 5.5 10.5 10.5M10.5 5.5 5.5 10.5"
                  stroke="#cf222e"
                  stroke-width="1.5"
                  stroke-linecap="round"
                />
              </svg>
              <span class="st-name">{{ tc.tool }}</span>
              <span v-if="tc.done" class="st-time">{{
                formatDuration(tc.durationMs)
              }}</span>
            </div>
          </div>
        </div>
      </div>

      <!-- Hover popover (positioned outside sidebar scroll) -->
      <Teleport to="body">
        <div
          v-if="hoveredTool"
          class="tool-popover"
          :style="popoverStyle"
          @click.stop
        >
          <div class="tool-popover-header">
            <span class="tool-popover-name">{{ hoveredTool.tool }}</span>
            <span class="tool-popover-time">{{
              formatDuration(hoveredTool.durationMs)
            }}</span>
          </div>
          <div v-if="hoveredTool.args" class="tool-popover-section">
            <div class="tool-popover-label">INPUT</div>
            <pre class="tool-popover-pre">{{
              formatJson(hoveredTool.args)
            }}</pre>
          </div>
          <div v-if="hoveredTool.result" class="tool-popover-section">
            <div class="tool-popover-label">RESPONSE</div>
            <pre class="tool-popover-pre">{{
              truncate(hoveredTool.result, 4000)
            }}</pre>
          </div>
          <div v-if="hoveredTool.error" class="tool-popover-section">
            <div class="tool-popover-label">ERROR</div>
            <pre class="tool-popover-pre tool-popover-pre--error">{{
              hoveredTool.error
            }}</pre>
          </div>
        </div>
      </Teleport>
      <!-- Bottom section: model selector + clear chat + user -->
      <div class="sidebar-footer">
        <!-- Model selector -->
        <div class="model-selector">
          <label class="model-selector-label">Model</label>
          <select v-model="selectedModel" class="model-selector-select">
            <option v-for="m in availableModels" :key="m" :value="m">
              {{ m }}
            </option>
          </select>
        </div>
        <button
          v-if="messages.length > 0 && !streaming"
          class="new-chat-btn"
          @click="clearMessages"
          title="New chat"
        >
          <svg
            xmlns="http://www.w3.org/2000/svg"
            width="14"
            height="14"
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            stroke-width="2"
            stroke-linecap="round"
            stroke-linejoin="round"
          >
            <path d="M3 12a9 9 0 1 0 9-9 9.75 9.75 0 0 0-6.74 2.74L3 8" />
            <path d="M3 3v5h5" />
          </svg>
          New Chat
        </button>
        <div class="sidebar-user">
          <img
            :src="user.avatar"
            :alt="user.login"
            class="sidebar-user-avatar"
          />
          <div class="sidebar-user-info">
            <span class="sidebar-user-name">{{ user.name || user.login }}</span>
            <span class="sidebar-user-login">@{{ user.login }}</span>
          </div>
          <button
            class="sidebar-logout-btn"
            @click="emit('logout')"
            title="Sign out"
          >
            <svg
              xmlns="http://www.w3.org/2000/svg"
              width="16"
              height="16"
              viewBox="0 0 24 24"
              fill="none"
              stroke="currentColor"
              stroke-width="2"
              stroke-linecap="round"
              stroke-linejoin="round"
            >
              <path d="M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4" />
              <polyline points="16 17 21 12 16 7" />
              <line x1="21" y1="12" x2="9" y2="12" />
            </svg>
          </button>
        </div>
      </div>
    </aside>

    <!-- Right: chat area -->
    <div class="chat-main">
      <!-- Messages -->
      <div class="messages" ref="messagesEl">
        <div class="messages-inner">
          <div v-if="messages.length === 0" class="empty-state">
            <h2 class="es-headline">Azure FinOps Agent</h2>
            <p class="es-sub">
              Ask about Azure pricing, compare costs across regions and SKUs, or
              check service health.
            </p>

            <div class="es-prompts">
              <button
                class="es-prompt"
                @click="
                  sendPrompt(
                    'We are deploying D4s_v5 VMs for our microservices platform. Show me a world map of Azure regions color-coded from cheapest to most expensive so we can pick the best region.',
                  )
                "
              >
                Regional cost map
              </button>
              <button
                class="es-prompt"
                @click="
                  sendPrompt(
                    'Our D16s_v5 VMs in East US are under 30% utilization. Compare D4s_v5, D8s_v5, and D16s_v5 monthly costs across East US, West Europe, and Southeast Asia. Show a grouped bar chart.',
                  )
                "
              >
                VM right-sizing
              </button>
              <button
                class="es-prompt"
                @click="
                  sendPrompt(
                    'Compare NC-series, ND-series, and NV-series GPU VM pricing in East US. Which gives best price per GPU? Show a chart.',
                  )
                "
              >
                GPU cost analysis
              </button>
              <button
                class="es-prompt"
                @click="
                  sendPrompt(
                    'Compare Azure Cosmos DB serverless vs Azure SQL Database General Purpose for a service handling 10M requests/day. Show a cost breakdown chart.',
                  )
                "
              >
                Database pricing
              </button>
              <button
                class="es-prompt"
                @click="
                  sendPrompt(
                    'Are there any active Azure service health incidents? We have workloads in East US, West Europe, and Southeast Asia.',
                  )
                "
              >
                Service health
              </button>
            </div>
          </div>

          <!-- Message list -->
          <div
            v-for="(msg, i) in messages"
            :key="i"
            class="message-row"
            :class="
              msg.role === 'user' ? 'message-row--user' : 'message-row--ai'
            "
          >
            <!-- User messages: right-aligned bubble -->
            <div v-if="msg.role === 'user'" class="bubble bubble--user">
              {{ msg.content }}
            </div>
            <!-- AI messages: left-aligned, no bubble -->
            <div v-else class="ai-row">
              <div class="ai-avatar">AI</div>
              <div class="ai-content">
                <!-- Charts -->
                <div
                  v-for="(chart, ci) in msg.charts || []"
                  :key="'chart-' + i + '-' + ci"
                  class="chart-container"
                  :ref="(el) => el && mountChart(el, chart)"
                ></div>
                <!-- Text content -->
                <div
                  class="message-text"
                  v-html="renderContent(msg.content)"
                ></div>
              </div>
            </div>
          </div>

          <!-- Streaming indicator -->
          <div v-if="streaming" class="message-row message-row--ai">
            <div class="ai-row">
              <div class="ai-avatar">AI</div>
              <div class="ai-content">
                <!-- Streaming charts -->
                <div
                  v-for="(chart, ci) in streamCharts"
                  :key="'stream-chart-' + ci"
                  class="chart-container"
                  :ref="(el) => el && mountChart(el, chart)"
                ></div>
                <div class="message-text">
                  <span
                    v-if="streamBuffer"
                    v-html="renderContent(streamBuffer)"
                  ></span>
                  <span class="streaming-cursor"></span>
                </div>
              </div>
            </div>
          </div>
        </div>
        <!-- /messages-inner -->
      </div>

      <!-- Input bar -->
      <div class="input-area">
        <div class="input-wrapper">
          <input
            ref="inputEl"
            v-model="input"
            type="text"
            @keydown.enter.prevent="send"
            placeholder="Ask about Azure pricing, cost comparisons, or FinOps insights..."
            class="input-field"
          />
          <button
            v-if="streaming"
            class="action-btn action-btn--stop"
            @click="stopGeneration"
          >
            <svg
              xmlns="http://www.w3.org/2000/svg"
              width="16"
              height="16"
              viewBox="0 0 24 24"
              fill="currentColor"
            >
              <rect x="6" y="6" width="12" height="12" rx="2" />
            </svg>
          </button>
          <button
            v-else
            class="action-btn"
            :class="
              input.trim() ? 'action-btn--active' : 'action-btn--disabled'
            "
            :disabled="!input.trim()"
            @click="send"
          >
            <svg
              xmlns="http://www.w3.org/2000/svg"
              width="16"
              height="16"
              viewBox="0 0 24 24"
              fill="none"
              stroke="currentColor"
              stroke-width="2.5"
              stroke-linecap="round"
              stroke-linejoin="round"
            >
              <line x1="12" y1="19" x2="12" y2="5" />
              <polyline points="5 12 12 5 19 12" />
            </svg>
          </button>
        </div>
      </div>
    </div>
    <!-- /chat-main -->

    <!-- Fixed build badge bottom-right -->
    <div class="build-badge">Build #{{ buildNumber }}</div>
  </div>
</template>

<script setup>
import * as echarts from "echarts";
import {
  computed,
  nextTick,
  onBeforeUnmount,
  onMounted,
  reactive,
  ref,
  watch,
} from "vue";

const props = defineProps({
  user: { type: Object, required: true },
});
const emit = defineEmits(["logout"]);

const messages = ref([]);
const input = ref("");
const streaming = ref(false);
const streamBuffer = ref("");
const activeTools = ref([]);
const streamToolCalls = ref([]);
const streamCharts = ref([]);
const messagesEl = ref(null);
const inputEl = ref(null);
const chartInstances = [];
const expandedToolIds = ref(new Set());
const hoveredTool = ref(null);
const collapsedSections = reactive({
  connected: false,
  prompts: false,
  tools: false,
});
function toggleSection(key) {
  collapsedSections[key] = !collapsedSections[key];
}
const buildSha = ref("");
const buildNumber = ref("0");
const showAgents = ref(false); // Set to true to re-enable Agents sidebar section
const availableModels = ref(["claude-sonnet-4.6"]);
const selectedModel = ref("claude-sonnet-4.6");

function dismissPopover() {
  hoveredTool.value = null;
}

const popoverStyle = computed(() => {
  return {
    left: "274px",
    top: "60px",
  };
});

onMounted(async () => {
  document.addEventListener("click", dismissPopover);
  try {
    const r = await fetch("/api/version");
    if (r.ok) {
      const v = await r.json();
      buildSha.value = v.sha || "";
      buildNumber.value = v.build || "0";
    }
  } catch {}
  // Fetch available models
  try {
    const r = await fetch("/api/models");
    if (r.ok) {
      const models = await r.json();
      if (Array.isArray(models) && models.length > 0) {
        const ids = models.map((m) => m.id || m.name || m).filter(Boolean);
        availableModels.value = ids;
        // Default to the highest-versioned Opus model if available
        const opusModels = ids.filter((id) => /opus/i.test(id));
        if (opusModels.length > 0) {
          opusModels.sort();
          selectedModel.value = opusModels[opusModels.length - 1];
        }
      }
    }
  } catch {}
});

let abortController = null;

async function clearMessages() {
  messages.value = [];
  streamBuffer.value = "";
  streamToolCalls.value = [];
  streamCharts.value = [];
  activeTools.value = [];
  expandedToolIds.value = new Set();
  chartInstances.forEach((c) => {
    try {
      c.dispose();
    } catch {}
  });
  chartInstances.length = 0;
  // Wait for backend to destroy the Copilot session before allowing new messages
  try {
    await fetch("/api/chat/reset", { method: "POST" });
  } catch {}
}

function stopGeneration() {
  if (abortController) {
    abortController.abort();
    abortController = null;
  }
}

const reversedToolCalls = computed(() => [...allToolCalls.value].reverse());
function formatDuration(ms) {
  if (ms == null) return "";
  if (ms < 1000) return ms + "ms";
  return (ms / 1000).toFixed(1) + "s";
}

// ── ECharts rendering ──

// World map GeoJSON cache
let worldMapLoaded = false;
let worldMapLoading = null;

async function ensureWorldMap() {
  if (worldMapLoaded) return;
  if (worldMapLoading) return worldMapLoading;
  worldMapLoading = fetch(
    "https://cdn.jsdelivr.net/npm/world-atlas@2/countries-110m.json",
  )
    .then((r) => r.json())
    .then((topoData) => {
      // Convert TopoJSON to GeoJSON for ECharts
      const countries = topojsonFeature(topoData, topoData.objects.countries);
      echarts.registerMap("world", countries);
      worldMapLoaded = true;
    })
    .catch(() => {
      // Fallback: try ECharts built-in world map URL
      return fetch("https://cdn.jsdelivr.net/npm/echarts@5/map/json/world.json")
        .then((r) => r.json())
        .then((geoJson) => {
          echarts.registerMap("world", geoJson);
          worldMapLoaded = true;
        });
    });
  return worldMapLoading;
}

// Minimal TopoJSON feature extraction (avoids importing topojson-client)
function topojsonFeature(topology, obj) {
  const arcs = topology.arcs;
  function decodeArc(arcIdx) {
    const arc = arcs[arcIdx < 0 ? ~arcIdx : arcIdx];
    const coords = [];
    let x = 0,
      y = 0;
    for (const [dx, dy] of arc) {
      x += dx;
      y += dy;
      coords.push([
        x * topology.transform.scale[0] + topology.transform.translate[0],
        y * topology.transform.scale[1] + topology.transform.translate[1],
      ]);
    }
    if (arcIdx < 0) coords.reverse();
    return coords;
  }
  function decodeRing(ring) {
    return ring.reduce((coords, arcIdx) => {
      const decoded = decodeArc(arcIdx);
      return coords.concat(decoded);
    }, []);
  }
  function decodeGeometry(geom) {
    if (geom.type === "Polygon") {
      return {
        type: "Polygon",
        coordinates: geom.arcs.map(decodeRing),
      };
    } else if (geom.type === "MultiPolygon") {
      return {
        type: "MultiPolygon",
        coordinates: geom.arcs.map((polygon) => polygon.map(decodeRing)),
      };
    }
    return geom;
  }
  const features = obj.geometries.map((geom) => ({
    type: "Feature",
    properties: geom.properties || {},
    geometry: decodeGeometry(geom),
  }));
  return { type: "FeatureCollection", features };
}

function buildEChartsOption(raw) {
  let parsed;
  try {
    parsed = typeof raw === "string" ? JSON.parse(raw) : raw;
  } catch {
    return null;
  }

  // Raw ECharts options mode (from RenderAdvancedChart)
  if (parsed.raw === true && parsed.options) {
    try {
      const opts =
        typeof parsed.options === "string"
          ? JSON.parse(parsed.options)
          : parsed.options;
      // Mark as needing map registration
      opts._needsMap = needsMapRegistration(opts);
      return opts;
    } catch {
      return null;
    }
  }

  let dataArr;
  try {
    dataArr =
      typeof parsed.data === "string" ? JSON.parse(parsed.data) : parsed.data;
  } catch {
    return null;
  }

  const chartType = (parsed.type || "bar").toLowerCase();
  const title = parsed.title || "";
  const seriesName = parsed.seriesName || "";
  const xAxisName = parsed.xAxisName || "";
  const yAxisName = parsed.yAxisName || "";

  const colors = [
    "#42A5F5",
    "#66BB6A",
    "#EF5350",
    "#FFA726",
    "#AB47BC",
    "#26C6DA",
    "#EC407A",
    "#8D6E63",
    "#78909C",
    "#D4E157",
  ];

  if (chartType === "pie" || chartType === "funnel") {
    const pieData = dataArr.map((d) =>
      Array.isArray(d) ? { name: String(d[0]), value: d[1] } : d,
    );
    return {
      title: {
        text: title,
        left: "center",
        textStyle: { fontSize: 14, color: "#1f2328" },
      },
      tooltip: { trigger: "item", formatter: "{b}: {c} ({d}%)" },
      legend: { bottom: 0, textStyle: { color: "#656d76", fontSize: 11 } },
      color: colors,
      series: [
        {
          name: seriesName,
          type: chartType,
          radius: chartType === "pie" ? "60%" : undefined,
          data: pieData,
          label: { fontSize: 11 },
        },
      ],
    };
  }

  // bar, line, scatter
  const categories = dataArr.map((d) =>
    Array.isArray(d) ? String(d[0]) : d.name,
  );

  // Detect multi-series: objects with keys beyond "name" and "value"
  const firstItem = dataArr[0];
  const isMultiSeries =
    firstItem &&
    !Array.isArray(firstItem) &&
    !("value" in firstItem) &&
    Object.keys(firstItem).filter((k) => k !== "name").length > 1;

  if (isMultiSeries) {
    const seriesKeys = Object.keys(firstItem).filter((k) => k !== "name");
    return {
      title: {
        text: title,
        left: "center",
        textStyle: { fontSize: 14, color: "#1f2328" },
      },
      tooltip: { trigger: "axis" },
      legend: {
        data: seriesKeys,
        bottom: 0,
        textStyle: { color: "#656d76", fontSize: 11 },
      },
      color: colors,
      grid: { left: 60, right: 20, bottom: 40, top: 50 },
      xAxis: {
        type: "category",
        data: categories,
        name: xAxisName,
        nameLocation: "center",
        nameGap: 30,
        axisLabel: { fontSize: 10, rotate: categories.length > 10 ? 45 : 0 },
      },
      yAxis: {
        type: "value",
        name: yAxisName,
        nameLocation: "center",
        nameGap: 45,
        axisLabel: { fontSize: 10 },
      },
      series: seriesKeys.map((key, idx) => ({
        name: key,
        type: chartType === "scatter" ? "scatter" : chartType,
        data: dataArr.map((d) => d[key]),
        barMaxWidth: 40,
      })),
    };
  }

  const values = dataArr.map((d) => (Array.isArray(d) ? d[1] : d.value));

  return {
    title: {
      text: title,
      left: "center",
      textStyle: { fontSize: 14, color: "#1f2328" },
    },
    tooltip: { trigger: "axis" },
    color: colors,
    grid: { left: 60, right: 20, bottom: 40, top: 50 },
    xAxis: {
      type: "category",
      data: categories,
      name: xAxisName,
      nameLocation: "center",
      nameGap: 30,
      axisLabel: { fontSize: 10, rotate: categories.length > 10 ? 45 : 0 },
    },
    yAxis: {
      type: "value",
      name: yAxisName,
      nameLocation: "center",
      nameGap: 45,
      axisLabel: { fontSize: 10 },
    },
    series: [
      {
        name: seriesName,
        type: chartType === "scatter" ? "scatter" : chartType,
        data: values,
        barMaxWidth: 40,
      },
    ],
  };
}

function needsMapRegistration(opts) {
  if (!opts) return false;
  if (worldMapLoaded) return false;
  // Check for geo config (scatter on world map)
  if (opts.geo) {
    const geos = Array.isArray(opts.geo) ? opts.geo : [opts.geo];
    if (geos.some((g) => g.map === "world")) return true;
  }
  // Check for map series
  if (opts.series) {
    const series = Array.isArray(opts.series) ? opts.series : [opts.series];
    if (series.some((s) => s.type === "map" && s.map === "world")) return true;
  }
  return false;
}

function mountChart(el, chartData) {
  if (!el || el._echarts_mounted) return;
  el._echarts_mounted = true;
  const option = buildEChartsOption(chartData);
  if (!option) return;

  const doMount = () => {
    nextTick(() => {
      const isMap =
        option._needsMap ||
        !!option.geo ||
        (option.series &&
          Array.isArray(option.series) &&
          option.series.some((s) => s.type === "map"));
      delete option._needsMap;
      const instance = echarts.init(el, null, {
        renderer: isMap ? "canvas" : "svg",
      });
      instance.setOption(option);
      chartInstances.push(instance);
      const ro = new ResizeObserver(() => instance.resize());
      ro.observe(el);
    });
  };

  if (option._needsMap) {
    ensureWorldMap().then(doMount).catch(doMount);
  } else {
    doMount();
  }
}

onBeforeUnmount(() => {
  chartInstances.forEach((c) => c.dispose());
  document.removeEventListener("click", dismissPopover);
});

// ── Aggregated tool calls for sidebar ──
const allToolCalls = computed(() => {
  const result = [];
  for (const msg of messages.value) {
    if (msg.toolCalls) {
      for (const tc of msg.toolCalls) {
        result.push({ ...tc, _uid: `msg-${tc.id}`, done: true });
      }
    }
  }
  for (const tc of streamToolCalls.value) {
    result.push({ ...tc, _uid: `stream-${tc.id}` });
  }
  return result;
});

// -- Suggested questions --
const suggestedQuestions = [
  {
    label: "Regional Cost Map",
    prompt:
      "We're deploying D4s_v5 VMs for our microservices platform. Show me a world map of Azure regions color-coded from cheapest (green) to most expensive (red) so we can pick the best region for cost and latency.",
  },
  {
    label: "VM Right-Sizing",
    prompt:
      "Our team is running D16s_v5 VMs in East US but utilization is under 30%. Compare the monthly cost of D4s_v5, D8s_v5, and D16s_v5 in East US, West Europe, and Southeast Asia. Show a grouped bar chart and recommend the best option.",
  },
  {
    label: "GPU Cost Analysis",
    prompt:
      "We need to run ML training jobs on Azure. Compare the hourly and monthly costs of NC-series, ND-series, and NV-series GPU VMs in East US. Which gives the best price per GPU? Show a chart.",
  },
  {
    label: "Database Pricing",
    prompt:
      "We're choosing between Azure Cosmos DB (serverless) and Azure SQL Database (General Purpose) for a new service handling 10M requests/day. Compare the pricing models and show a cost breakdown chart.",
  },
  {
    label: "App Service vs AKS",
    prompt:
      "Compare the monthly cost of running a .NET web app on App Service Premium v3 P1V3 vs an equivalent AKS cluster with 2 D4s_v5 nodes in East US. Include compute, networking, and load balancer costs.",
  },
  {
    label: "Service Health",
    prompt:
      "Are there any active Azure service health incidents or degradations? We have workloads in East US, West Europe, and Southeast Asia — flag anything affecting those regions.",
  },
  {
    label: "Available D-series VMs",
    prompt:
      "List all available D-series VM sizes in France Central with their vCPUs, RAM, and hourly Linux pay-as-you-go pricing. Sort by price ascending.",
  },
];

function sendQuestion(q) {
  if (streaming.value) return;
  input.value = q;
  send();
}

// -- Connected sources --
const connectedSources = [
  { name: "Azure Retail Prices" },
  { name: "Azure Service Health" },
];

function formatJson(str) {
  try {
    return JSON.stringify(JSON.parse(str), null, 2);
  } catch {
    return str;
  }
}

function truncate(str, max) {
  if (!str || str.length <= max) return str;
  return str.slice(0, max) + "\n... (truncated)";
}

function scrollToBottom() {
  nextTick(() => {
    if (messagesEl.value) {
      messagesEl.value.scrollTo({
        top: messagesEl.value.scrollHeight,
        behavior: "smooth",
      });
    }
  });
}

watch(() => messages.value.length, scrollToBottom);
watch(streamBuffer, scrollToBottom);
watch(() => streamToolCalls.value.length, scrollToBottom);

watch(streaming, async (val) => {
  if (!val) {
    await nextTick();
    inputEl.value?.focus();
  }
});

function renderContent(text) {
  if (!text) return "";
  let html = text.replace(
    /```(\w*)\n([\s\S]*?)```/g,
    '<pre><code class="lang-$1">$2</code></pre>',
  );
  html = html.replace(
    /((?:^|\n)\|.+\|(?:\n\|[-:| ]+\|)(?:\n\|.+\|)+)/g,
    (match) => {
      const lines = match.trim().split("\n");
      if (lines.length < 2) return match;
      const headers = lines[0]
        .split("|")
        .filter((c) => c.trim())
        .map((c) => `<th>${c.trim()}</th>`)
        .join("");
      const rows = lines
        .slice(2)
        .map((row) => {
          const cells = row
            .split("|")
            .filter((c) => c.trim())
            .map((c) => `<td>${c.trim()}</td>`)
            .join("");
          return `<tr>${cells}</tr>`;
        })
        .join("");
      return `<table><thead><tr>${headers}</tr></thead><tbody>${rows}</tbody></table>`;
    },
  );
  html = html.replace(/^### (.+)$/gm, "<h4>$1</h4>");
  html = html.replace(/^## (.+)$/gm, "<h3>$1</h3>");
  html = html.replace(/^# (.+)$/gm, "<h2>$1</h2>");
  html = html.replace(/`([^`]+)`/g, "<code>$1</code>");
  html = html.replace(/\*\*(.+?)\*\*/g, "<strong>$1</strong>");
  html = html.replace(/\*(.+?)\*/g, "<em>$1</em>");
  html = html.replace(/^- (.+)$/gm, "<li>$1</li>");
  html = html.replace(/((?:<li>.*<\/li>\n?)+)/g, "<ul>$1</ul>");
  html = html.replace(/\n/g, "<br/>");
  html = html.replace(/<\/(table|pre|ul|h[234])><br\/>/g, "</$1>");
  html = html.replace(/<br\/><(table|pre|ul|h[234])/g, "<$1");
  return html;
}

function sendPrompt(text) {
  input.value = text;
  send();
}

async function send() {
  const prompt = input.value.trim();
  if (!prompt || streaming.value) return;

  messages.value.push({ role: "user", content: prompt });
  input.value = "";
  streaming.value = true;
  streamBuffer.value = "";
  activeTools.value = [];
  streamToolCalls.value = [];
  scrollToBottom();

  abortController = new AbortController();
  const toolCalls = [];
  try {
    const res = await fetch("/api/chat", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ prompt, model: selectedModel.value }),
      signal: abortController.signal,
    });

    const reader = res.body.getReader();
    const decoder = new TextDecoder();
    let buffer = "";

    while (true) {
      const { done, value } = await reader.read();
      if (done) break;

      buffer += decoder.decode(value, { stream: true });
      const lines = buffer.split("\n");
      buffer = lines.pop() || "";

      for (const line of lines) {
        if (!line.startsWith("data: ")) continue;
        const payload = line.slice(6);
        if (payload === "[DONE]") break;

        try {
          const data = JSON.parse(payload);
          switch (data.type) {
            case "delta":
              streamBuffer.value += data.content;
              break;
            case "message":
              // Append rather than replace — the SDK may send a complete
              // message after tool calls that would wipe out earlier streamed
              // content (tables, analysis) if we overwrote the buffer.
              if (data.content) {
                streamBuffer.value += data.content;
              }
              break;
            case "tool_start": {
              activeTools.value.push(data.tool);
              const tc = {
                id: data.id,
                tool: data.tool,
                args: data.args || null,
                result: null,
                error: null,
                success: null,
                durationMs: null,
                done: false,
                expanded: false,
              };
              toolCalls.push(tc);
              streamToolCalls.value = [...toolCalls];
              break;
            }
            case "tool_done": {
              activeTools.value = activeTools.value.filter(
                (t) => t !== data.tool,
              );
              const existing = toolCalls.find((t) => t.id === data.id);
              if (existing) {
                existing.done = true;
                existing.success = data.success;
                existing.durationMs = data.durationMs;
                existing.result = data.result || null;
                existing.error = data.error || null;
              }
              streamToolCalls.value = [...toolCalls];
              break;
            }
            case "chart": {
              streamCharts.value = [...streamCharts.value, data.options];
              scrollToBottom();
              break;
            }
            case "error":
              streamBuffer.value += `\n⚠️ Error: ${data.message}`;
              break;
          }
        } catch {}
      }
    }

    messages.value.push({
      role: "assistant",
      content: streamBuffer.value,
      toolCalls: toolCalls.map((tc) => ({ ...tc, expanded: false })),
      charts: [...streamCharts.value],
    });
  } catch (err) {
    if (err.name === "AbortError") {
      if (streamBuffer.value) {
        messages.value.push({
          role: "assistant",
          content: streamBuffer.value + "\n\n*(generation stopped)*",
          toolCalls: toolCalls.map((tc) => ({ ...tc, expanded: false })),
          charts: [...streamCharts.value],
        });
      }
    } else {
      messages.value.push({
        role: "assistant",
        content: `⚠️ Connection error: ${err.message}`,
      });
    }
  } finally {
    streaming.value = false;
    streamBuffer.value = "";
    activeTools.value = [];
    streamToolCalls.value = [];
    streamCharts.value = [];
    abortController = null;
    nextTick(() => inputEl.value?.focus());
    if (availableModels.value.length <= 1) {
      try {
        const mr = await fetch("/api/models");
        if (mr.ok) {
          const models = await mr.json();
          if (Array.isArray(models) && models.length > 0) {
            availableModels.value = models
              .map((m) => m.id || m.name || m)
              .filter(Boolean);
          }
        }
      } catch {}
    }
  }
}
</script>

<style scoped>
.chat-view {
  display: flex;
  flex-direction: row;
  height: 100%;
  min-height: 0;
  overflow: hidden;
}

/* ── Left sidebar ── */
.sidebar {
  width: 260px;
  flex-shrink: 0;
  border-right: 1px solid var(--border);
  background: var(--surface);
  display: flex;
  flex-direction: column;
  overflow: hidden;
}

.sidebar-scroll {
  flex: 1;
  overflow-y: auto;
  padding: 12px 14px;
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.sidebar-category-label {
  font-size: 11px;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.06em;
  color: var(--text-muted);
  margin-bottom: 6px;
  padding: 0 6px;
}

.sidebar-category-label--toggle {
  display: flex;
  align-items: center;
  justify-content: space-between;
  cursor: pointer;
  user-select: none;
  border-radius: 4px;
  padding: 2px 6px;
  margin-left: -2px;
  margin-right: -2px;
}

.sidebar-category-label--toggle:hover {
  background: rgba(0, 0, 0, 0.04);
}

.collapse-chevron {
  width: 14px;
  height: 14px;
  transition: transform 0.15s ease;
  flex-shrink: 0;
}

.collapse-chevron--collapsed {
  transform: rotate(-90deg);
}

.collapse-body {
  overflow: hidden;
  max-height: 600px;
  opacity: 1;
  transition:
    max-height 0.3s cubic-bezier(0.4, 0, 0.2, 1),
    opacity 0.25s ease;
}

.collapse-body--collapsed {
  max-height: 0;
  opacity: 0;
  transition:
    max-height 0.25s cubic-bezier(0.4, 0, 0.2, 1),
    opacity 0.15s ease;
}

.sidebar-agent {
  display: flex;
  align-items: center;
  gap: 10px;
  width: 100%;
  padding: 7px 10px;
  border: none;
  border-radius: 8px;
  background: transparent;
  font-size: 14px;
  font-weight: 400;
  color: var(--text);
  margin-bottom: 0;
}

.sidebar-agent-icon {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 30px;
  height: 30px;
  border-radius: 6px;
  flex-shrink: 0;
}

.sidebar-agent-icon--claude {
  background: #fef0e8;
}

.sidebar-agent-icon--codex {
  background: #f0f0f0;
}

/* ── Chat main area ── */
.chat-main {
  flex: 1;
  display: flex;
  flex-direction: column;
  min-width: 0;
  min-height: 0;
  overflow: hidden;
}

/* ── Sidebar tool calls ── */
.sidebar-tools {
  margin-top: 6px;
  border-top: 1px solid var(--border);
  padding-top: 10px;
  font-family: "Cascadia Code", "Fira Code", "Consolas", monospace;
  font-size: 0.75rem;
  min-height: 0;
  display: flex;
  flex-direction: column;
  overflow: hidden;
}

.st-list {
  overflow-y: auto;
  scrollbar-width: thin;
  scrollbar-color: rgba(0, 0, 0, 0.1) transparent;
}

.sidebar-tools .sidebar-category-label {
  display: flex;
  align-items: center;
  justify-content: space-between;
}

.st-count {
  font-size: 10px;
  font-weight: 600;
  background: #e8e8e8;
  color: var(--text-muted);
  border-radius: 9999px;
  padding: 1px 7px;
  min-width: 20px;
  text-align: center;
}

.st-icon {
  width: 15px;
  height: 15px;
  flex-shrink: 0;
}

.st-icon--spin {
  animation: icon-spin 0.9s linear infinite;
}

@keyframes icon-spin {
  to {
    transform: rotate(360deg);
  }
}

.st-icon--ok {
  animation: icon-pop 0.3s ease-out;
}

@keyframes icon-pop {
  0% {
    transform: scale(0);
    opacity: 0;
  }
  60% {
    transform: scale(1.2);
  }
  100% {
    transform: scale(1);
    opacity: 1;
  }
}

.st-row {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 5px 10px;
  border-radius: 6px;
  cursor: default;
  user-select: none;
  transition: background 0.1s;
  min-height: 28px;
  position: relative;
}

.st-row:hover {
  background: rgba(9, 105, 218, 0.06);
}

.st-row--clickable {
  cursor: pointer;
}

.st-row--running {
  cursor: default;
  background: rgba(191, 135, 0, 0.06);
}

.st-row--running:hover {
  background: rgba(191, 135, 0, 0.06);
}

.st-name {
  flex: 1;
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  color: var(--text);
  font-weight: 500;
}

.st-time {
  flex-shrink: 0;
  color: var(--text-muted);
  font-size: 0.65rem;
  font-weight: 400;
}

/* ── Hover popover ── */
.tool-popover {
  position: fixed;
  z-index: 9999;
  width: 860px;
  max-height: 85vh;
  background: #fff;
  border: 1px solid var(--border, #d8dee4);
  border-radius: 10px;
  box-shadow:
    0 8px 30px rgba(0, 0, 0, 0.12),
    0 2px 8px rgba(0, 0, 0, 0.06);
  padding: 0;
  overflow: hidden;
  display: flex;
  flex-direction: column;
  animation: popover-in 0.15s ease-out;
  font-family: "Cascadia Code", "Fira Code", "Consolas", monospace;
}

@keyframes popover-in {
  from {
    opacity: 0;
    transform: translateX(-6px);
  }
  to {
    opacity: 1;
    transform: translateX(0);
  }
}

.tool-popover-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 1rem 1.4rem;
  border-bottom: 1px solid var(--border, #d8dee4);
  background: #fafbfc;
}

.tool-popover-name {
  font-size: 1.15rem;
  font-weight: 600;
  color: #1f2328;
}

.tool-popover-time {
  font-size: 0.95rem;
  color: #656d76;
}

.tool-popover-section {
  padding: 0.85rem 1.4rem;
  border-bottom: 1px solid rgba(0, 0, 0, 0.04);
}

.tool-popover-section:last-child {
  border-bottom: none;
}

.tool-popover-label {
  font-size: 0.82rem;
  font-weight: 700;
  letter-spacing: 0.08em;
  color: #656d76;
  margin-bottom: 0.4rem;
  text-transform: uppercase;
}

.tool-popover-pre {
  background: #f6f8fa;
  border: 1px solid #d8dee4;
  border-radius: 6px;
  padding: 0.85rem 1rem;
  margin: 0;
  font-size: 1rem;
  white-space: pre-wrap;
  word-break: break-word;
  max-height: 420px;
  overflow-y: auto;
  line-height: 1.6;
  color: #1f2328;
  scrollbar-width: thin;
  scrollbar-color: rgba(0, 0, 0, 0.1) transparent;
}

.tool-popover-pre::-webkit-scrollbar {
  width: 4px;
}

.tool-popover-pre::-webkit-scrollbar-thumb {
  background: rgba(0, 0, 0, 0.1);
  border-radius: 2px;
}

.tool-popover-pre--error {
  color: #cf222e;
  background: #fef2f2;
  border-color: rgba(186, 26, 26, 0.15);
}

.messages {
  flex: 1;
  overflow-y: auto;
  min-height: 0;
  display: flex;
  flex-direction: column;
  scroll-behavior: smooth;
}

.messages-inner {
  padding: 1rem 2rem;
  display: flex;
  flex-direction: column;
  gap: 1rem;
  width: 100%;
  max-width: 100%;
  min-width: 0;
  flex: 1;
}

.empty-state {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  flex: 1;
  gap: 0;
  color: var(--text-muted);
  padding: 3rem 1.5rem;
  max-width: 780px;
  margin: 0 auto;
  width: 100%;
  animation: fadeSlideIn 0.4s cubic-bezier(0.4, 0, 0.2, 1);
}

@keyframes fadeSlideIn {
  from {
    opacity: 0;
    transform: translateY(12px);
  }
  to {
    opacity: 1;
    transform: translateY(0);
  }
}

.es-avatar {
  width: 96px;
  height: 96px;
  border-radius: 50%;
  background: #18181b;
  color: #fff;
  font-size: 32px;
  font-weight: 700;
  display: flex;
  align-items: center;
  justify-content: center;
  margin-bottom: 1.25rem;
  letter-spacing: 2px;
}

.es-headline {
  font-size: 2rem;
  font-weight: 700;
  color: var(--text-primary, #1f2328);
  margin: 0 0 0.5rem;
  letter-spacing: -0.02em;
}

.es-sub {
  font-size: 1.1rem;
  color: var(--text-muted);
  margin: 0 0 2rem;
  line-height: 1.5;
}

/* Phases row */
.es-phases {
  display: flex;
  gap: 1rem;
  width: 100%;
  margin-bottom: 1.75rem;
}

.es-phase {
  flex: 1;
  display: flex;
  gap: 0.75rem;
  align-items: flex-start;
  padding: 1rem 1.15rem;
  border: 1px solid var(--border, #d8dee4);
  border-radius: 10px;
  font-size: 0.9rem;
  line-height: 1.45;
  color: var(--text-primary, #1f2328);
}

.es-phase-num {
  width: 26px;
  height: 26px;
  border-radius: 50%;
  background: #0969da;
  color: #fff;
  font-size: 0.8rem;
  font-weight: 700;
  display: flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
  margin-top: 1px;
}

.es-phase strong {
  display: block;
  font-weight: 600;
  margin-bottom: 3px;
  font-size: 0.95rem;
}

.es-phase-desc {
  display: block;
  color: var(--text-muted);
  font-size: 0.85rem;
}

/* Prompt suggestion chips */
.es-prompts {
  display: flex;
  flex-wrap: wrap;
  gap: 0.5rem;
  width: 100%;
  justify-content: center;
}

.es-prompt {
  padding: 0.55rem 1rem;
  border: 1px solid var(--border, #d8dee4);
  border-radius: 20px;
  background: transparent;
  color: var(--text-primary, #1f2328);
  font-size: 0.85rem;
  cursor: pointer;
  transition: all 0.15s ease;
}

.es-prompt:hover {
  background: var(--bg-hover, #f3f4f6);
  border-color: #0969da;
  color: #0969da;
}

/* Sidebar suggested questions */
.sidebar-questions {
  margin-top: 6px;
  border-top: 1px solid var(--border);
  padding-top: 10px;
}

.sidebar-question {
  display: flex;
  align-items: center;
  gap: 10px;
  width: 100%;
  padding: 7px 10px;
  border: none;
  border-radius: 8px;
  background: transparent;
  font-size: 14px;
  font-weight: 400;
  color: var(--text);
  cursor: pointer;
  text-align: left;
  line-height: 1.4;
  transition: all 0.15s;
  font-family: inherit;
}

.sidebar-question-icon {
  flex-shrink: 0;
  width: 24px;
  height: 24px;
  border-radius: 50%;
  background: #18181b;
  color: #fff;
  font-size: 9px;
  font-weight: 700;
  letter-spacing: 0.3px;
  display: flex;
  align-items: center;
  justify-content: center;
}

.sidebar-question:hover {
  background: rgba(9, 105, 218, 0.06);
  color: var(--accent);
}

.sidebar-question:disabled {
  opacity: 0.4;
  cursor: default;
}

/* Sidebar connected sources */
.sidebar-sources {
  margin-top: 6px;
  border-top: 1px solid var(--border);
  padding-top: 10px;
}

.sidebar-source {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 7px 10px;
  font-size: 14px;
  color: var(--text);
}

.sidebar-source-dot {
  width: 7px;
  height: 7px;
  border-radius: 50%;
  flex-shrink: 0;
  background: #2da44e;
  box-shadow: 0 0 4px rgba(45, 164, 78, 0.45);
}

/* ── Messages ── */
.message-row {
  display: flex;
  width: 100%;
  min-width: 0;
  max-width: 100%;
  animation: messageSlideIn 0.3s cubic-bezier(0.4, 0, 0.2, 1);
}

@keyframes messageSlideIn {
  from {
    opacity: 0;
    transform: translateY(8px);
  }
  to {
    opacity: 1;
    transform: translateY(0);
  }
}

.message-row--user {
  justify-content: flex-end;
}

.message-row--ai {
  justify-content: flex-start;
}

.bubble--user {
  max-width: 80%;
  border-radius: 1rem;
  padding: 0.75rem 1rem;
  background: #f4f4f5;
  color: #18181b;
  font-size: 0.9rem;
  line-height: 1.5;
  word-wrap: break-word;
}

.ai-row {
  display: flex;
  flex-direction: column;
  gap: 0.35rem;
  max-width: 100%;
  width: 100%;
  min-width: 0;
}

.ai-avatar {
  flex-shrink: 0;
  width: 28px;
  height: 28px;
  border-radius: 50%;
  background: #18181b;
  color: #fff;
  font-size: 11px;
  font-weight: 700;
  display: flex;
  align-items: center;
  justify-content: center;
  margin-top: 2px;
  letter-spacing: 0.5px;
}

.ai-content {
  min-width: 0;
  max-width: 100%;
  overflow-x: auto;
}

.message-text {
  font-size: 0.9rem;
  line-height: 1.6;
  word-wrap: break-word;
}

.streaming-cursor {
  display: inline-block;
  width: 6px;
  height: 16px;
  background: #71717a;
  border-radius: 1px;
  margin-left: 2px;
  vertical-align: text-bottom;
  animation: cursor-pulse 1s ease-in-out infinite;
}

@keyframes cursor-pulse {
  0%,
  100% {
    opacity: 1;
  }
  50% {
    opacity: 0;
  }
}

.message-text :deep(pre) {
  background: var(--surface);
  border: 1px solid var(--border);
  border-radius: 6px;
  padding: 0.75rem 1rem;
  margin: 0.5rem 0;
  overflow-x: auto;
  font-size: 0.85rem;
}

.message-text :deep(code) {
  background: rgba(175, 184, 193, 0.2);
  padding: 0.15rem 0.35rem;
  border-radius: 3px;
  font-size: 0.85em;
  font-family: "Cascadia Code", "Fira Code", "Consolas", monospace;
}

.message-text :deep(pre code) {
  background: none;
  padding: 0;
}

.message-text :deep(table) {
  border-collapse: collapse;
  width: 100%;
  margin: 0.5rem 0;
  font-size: 0.82rem;
  display: block;
  overflow-x: auto;
}

.message-text :deep(th),
.message-text :deep(td) {
  border: 1px solid var(--border);
  padding: 0.35rem 0.6rem;
  text-align: left;
}

.message-text :deep(th) {
  background: var(--surface);
  font-weight: 600;
  font-size: 0.78rem;
}

.message-text :deep(tr:nth-child(even)) {
  background: rgba(246, 248, 250, 0.5);
}

.message-text :deep(h2),
.message-text :deep(h3),
.message-text :deep(h4) {
  margin: 0.75rem 0 0.25rem;
  font-weight: 600;
}

.message-text :deep(h2) {
  font-size: 1.1rem;
}
.message-text :deep(h3) {
  font-size: 1rem;
}
.message-text :deep(h4) {
  font-size: 0.92rem;
}

.message-text :deep(ul) {
  margin: 0.25rem 0;
  padding-left: 1.5rem;
}

.message-text :deep(li) {
  margin: 0.15rem 0;
}

@media (max-width: 768px) {
  .sidebar {
    display: none;
  }

  .tool-popover {
    display: none;
  }
}

/* ── Chart containers ── */
.chart-container {
  width: 100%;
  height: 340px;
  margin: 0 0 0.75rem;
  border: 1px solid var(--border);
  border-radius: 8px;
  background: #fff;
  overflow: hidden;
}

/* ── Input area (pill style) ── */
.input-area {
  flex-shrink: 0;
  padding: 0.75rem 1rem;
  max-width: 800px;
  margin: 0 auto;
  width: 100%;
  padding-bottom: max(0.75rem, env(safe-area-inset-bottom));
}

.input-wrapper {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  border: 1px solid #d4d4d8;
  border-radius: 9999px;
  padding: 0.5rem 0.5rem 0.5rem 1rem;
  background: #fff;
  transition: border-color 0.15s;
}

.input-wrapper:focus-within {
  border-color: #a1a1aa;
}

.input-field {
  flex: 1;
  background: transparent;
  border: none;
  color: #18181b;
  font-size: 0.875rem;
  font-family: inherit;
  padding: 0;
  outline: none;
  line-height: 1.5;
}

.input-field::placeholder {
  color: #a1a1aa;
}

/* Action buttons (send / stop) */
.action-btn {
  flex-shrink: 0;
  width: 32px;
  height: 32px;
  display: flex;
  align-items: center;
  justify-content: center;
  border-radius: 9999px;
  border: none;
  cursor: pointer;
  transition: all 0.15s;
}

.action-btn--active {
  background: #18181b;
  color: #fff;
}

.action-btn--active:hover {
  background: #3f3f46;
}

.action-btn--disabled {
  background: #e4e4e7;
  color: #a1a1aa;
  cursor: default;
}

.action-btn--stop {
  background: #18181b;
  color: #fff;
}

.action-btn--stop:hover {
  background: #3f3f46;
}

.sidebar-footer {
  flex-shrink: 0;
  padding: 8px 10px;
  border-top: 1px solid var(--border);
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.new-chat-btn {
  display: flex;
  align-items: center;
  gap: 6px;
  width: 100%;
  padding: 7px 12px;
  border-radius: 8px;
  border: 1px solid var(--border);
  background: var(--surface);
  color: var(--text);
  font-size: 13px;
  font-weight: 500;
  cursor: pointer;
  transition: all 0.15s;
  flex-shrink: 0;
}

.new-chat-btn:hover {
  background: rgba(9, 105, 218, 0.06);
  border-color: #0969da;
  color: #0969da;
}

.sidebar-user {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 4px 4px;
}

.sidebar-user-avatar {
  width: 28px;
  height: 28px;
  border-radius: 50%;
  flex-shrink: 0;
}

.sidebar-user-info {
  display: flex;
  flex-direction: column;
  flex: 1;
  min-width: 0;
  line-height: 1.2;
}

.sidebar-user-name {
  font-size: 12px;
  font-weight: 500;
  color: var(--text);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.sidebar-user-login {
  font-size: 11px;
  color: var(--text-muted);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.sidebar-logout-btn {
  flex-shrink: 0;
  background: transparent;
  border: none;
  color: var(--text-muted);
  cursor: pointer;
  padding: 4px;
  border-radius: 6px;
  display: flex;
  align-items: center;
  justify-content: center;
  transition: all 0.15s;
}

.sidebar-logout-btn:hover {
  color: var(--red, #cf222e);
  background: rgba(207, 34, 46, 0.08);
}

.model-selector {
  padding: 4px 0 8px;
  border-bottom: 1px solid var(--border);
  margin-bottom: 8px;
}

.model-selector-label {
  display: block;
  font-size: 10px;
  font-weight: 600;
  color: var(--text-muted);
  text-transform: uppercase;
  letter-spacing: 0.5px;
  margin-bottom: 4px;
}

.model-selector-select {
  width: 100%;
  padding: 5px 8px;
  font-size: 12px;
  border: 1px solid var(--border);
  border-radius: 6px;
  background: var(--bg);
  color: var(--text);
  cursor: pointer;
  outline: none;
  transition: border-color 0.15s;
}

.model-selector-select:focus {
  border-color: var(--accent, #0969da);
}

.build-badge {
  position: fixed;
  bottom: 8px;
  right: 12px;
  font-size: 18px;
  font-family: monospace;
  color: #1f2328;
  opacity: 0.7;
  pointer-events: none;
  z-index: 1000;
  font-weight: 600;
}
</style>
