<template>
  <div class="chat-view">
    <!-- Left sidebar -->
    <aside class="sidebar">
      <div class="sidebar-scroll">
        <!-- FinOps Prompt Samples (require Azure) -->
        <div class="sidebar-category">
          <div
            class="sidebar-category-label sidebar-category-label--toggle"
            @click="toggleSection('finops')"
          >
            <span>FinOps</span>
            <svg
              class="collapse-chevron"
              :class="{
                'collapse-chevron--collapsed': collapsedSections.finops,
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
            :class="{ 'collapse-body--collapsed': collapsedSections.finops }"
          >
            <button
              v-for="q in finopsPrompts"
              :key="q.label"
              class="sidebar-question"
              :class="{ 'sidebar-question--locked': !azureConnected }"
              :disabled="streaming || !azureConnected"
              :title="azureConnected ? q.prompt : 'Connect Azure to unlock'"
              @click="sendQuestion(q.prompt)"
            >
              <span class="sidebar-question-icon sidebar-question-icon--finops">
                <svg
                  v-if="!azureConnected"
                  width="10"
                  height="10"
                  viewBox="0 0 16 16"
                  fill="currentColor"
                >
                  <path
                    d="M4 7V5a4 4 0 118 0v2h1a1 1 0 011 1v6a1 1 0 01-1 1H3a1 1 0 01-1-1V8a1 1 0 011-1h1zm2 0h4V5a2 2 0 10-4 0v2z"
                  />
                </svg>
                <template v-else>F</template>
              </span>
              <span>{{ q.label }}</span>
            </button>
          </div>
        </div>

        <!-- Pricing Prompt Samples (always available) -->
        <div class="sidebar-category sidebar-category--border">
          <div
            class="sidebar-category-label sidebar-category-label--toggle"
            @click="toggleSection('pricing')"
          >
            <span>Pricing</span>
            <svg
              class="collapse-chevron"
              :class="{
                'collapse-chevron--collapsed': collapsedSections.pricing,
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
            :class="{ 'collapse-body--collapsed': collapsedSections.pricing }"
          >
            <button
              v-for="q in pricingPrompts"
              :key="q.label"
              class="sidebar-question"
              :class="{ 'sidebar-question--locked': !user }"
              :disabled="streaming || !user"
              :title="user ? q.prompt : 'Sign in with GitHub to unlock'"
              @click="sendQuestion(q.prompt)"
            >
              <span class="sidebar-question-icon sidebar-question-icon--price">
                <svg
                  v-if="!user"
                  width="10"
                  height="10"
                  viewBox="0 0 16 16"
                  fill="currentColor"
                >
                  <path
                    d="M4 7V5a4 4 0 118 0v2h1a1 1 0 011 1v6a1 1 0 01-1 1H3a1 1 0 01-1-1V8a1 1 0 011-1h1zm2 0h4V5a2 2 0 10-4 0v2z"
                  />
                </svg>
                <template v-else>$</template>
              </span>
              <span>{{ q.label }}</span>
            </button>
          </div>
        </div>

        <!-- Tenant Prompt Samples (require Azure) -->
        <div class="sidebar-category sidebar-category--border">
          <div
            class="sidebar-category-label sidebar-category-label--toggle"
            @click="toggleSection('tenant')"
          >
            <span>Tenant</span>
            <svg
              class="collapse-chevron"
              :class="{
                'collapse-chevron--collapsed': collapsedSections.tenant,
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
            :class="{ 'collapse-body--collapsed': collapsedSections.tenant }"
          >
            <button
              v-for="q in tenantPrompts"
              :key="q.label"
              class="sidebar-question"
              :class="{ 'sidebar-question--locked': !azureConnected }"
              :disabled="streaming || !azureConnected"
              :title="azureConnected ? q.prompt : 'Connect Azure to unlock'"
              @click="sendQuestion(q.prompt)"
            >
              <span class="sidebar-question-icon sidebar-question-icon--tenant">
                <svg
                  v-if="!azureConnected"
                  width="10"
                  height="10"
                  viewBox="0 0 16 16"
                  fill="currentColor"
                >
                  <path
                    d="M4 7V5a4 4 0 118 0v2h1a1 1 0 011 1v6a1 1 0 01-1 1H3a1 1 0 01-1-1V8a1 1 0 011-1h1zm2 0h4V5a2 2 0 10-4 0v2z"
                  />
                </svg>
                <template v-else>T</template>
              </span>
              <span>{{ q.label }}</span>
            </button>
          </div>
        </div>

        <!-- Connected APIs -->
        <div class="sidebar-category sidebar-category--border">
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
            <div v-for="s in publicSources" :key="s" class="sidebar-source">
              <span class="sidebar-source-dot"></span>
              <span>{{ s }}</span>
            </div>
            <template v-if="azureConnected">
              <div class="sidebar-source-divider">Azure APIs</div>
              <div v-for="api in azureApis" :key="api" class="sidebar-source">
                <span
                  class="sidebar-source-dot sidebar-source-dot--azure"
                ></span>
                <span>{{ api }}</span>
              </div>
            </template>
          </div>
        </div>

        <!-- Subscriptions (after Azure login) -->
        <div
          v-if="azureConnected && azureSubscriptions.length"
          class="sidebar-category sidebar-category--border"
        >
          <div
            class="sidebar-category-label sidebar-category-label--toggle"
            @click="toggleSection('subs')"
          >
            <span>Subscriptions ({{ azureSubscriptions.length }})</span>
            <svg
              class="collapse-chevron"
              :class="{ 'collapse-chevron--collapsed': collapsedSections.subs }"
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
            :class="{ 'collapse-body--collapsed': collapsedSections.subs }"
          >
            <div
              v-for="sub in azureSubscriptions"
              :key="sub.id"
              class="sidebar-sub"
            >
              <span class="sidebar-sub-name">{{ sub.name }}</span>
              <span class="sidebar-sub-id"
                >{{ sub.id.substring(0, 8) }}...</span
              >
            </div>
          </div>
        </div>
      </div>

      <!-- Bottom section: Azure connect + model + user -->
      <div class="sidebar-footer">
        <!-- Not logged in: show GitHub login -->
        <template v-if="!user">
          <a href="/auth/github" class="login-btn login-btn--github">
            <svg width="16" height="16" viewBox="0 0 16 16" fill="currentColor">
              <path
                d="M8 0c4.42 0 8 3.58 8 8a8.013 8.013 0 0 1-5.45 7.59c-.4.08-.55-.17-.55-.38 0-.27.01-1.13.01-2.2 0-.75-.25-1.23-.54-1.48 1.78-.2 3.65-.88 3.65-3.95 0-.88-.31-1.59-.82-2.15.08-.2.36-1.02-.08-2.12 0 0-.67-.22-2.2.82-.64-.18-1.32-.27-2-.27-.68 0-1.36.09-2 .27-1.53-1.03-2.2-.82-2.2-.82-.44 1.1-.16 1.92-.08 2.12-.51.56-.82 1.28-.82 2.15 0 3.06 1.86 3.75 3.64 3.95-.23.2-.44.55-.51 1.07-.46.21-1.61.55-2.33-.66-.15-.24-.6-.83-1.23-.82-.67.01-.27.38.01.53.34.19.73.9.82 1.13.16.45.68 1.31 2.69.94 0 .67.01 1.3.01 1.49 0 .21-.15.45-.55.38A7.995 7.995 0 0 1 0 8c0-4.42 3.58-8 8-8Z"
              />
            </svg>
            Sign in with GitHub
          </a>
        </template>

        <!-- Logged in -->
        <template v-else>
          <!-- Azure connect/status -->
          <div v-if="!azureConnected" class="azure-connect">
            <a href="/auth/microsoft" class="azure-connect-btn">
              <svg width="16" height="16" viewBox="0 0 21 21" fill="none">
                <rect width="10" height="10" fill="#f25022" />
                <rect x="11" width="10" height="10" fill="#7fba00" />
                <rect y="11" width="10" height="10" fill="#00a4ef" />
                <rect x="11" y="11" width="10" height="10" fill="#ffb900" />
              </svg>
              Connect Azure
            </a>
          </div>
          <div v-else class="azure-status">
            <div class="azure-status-info">
              <svg width="14" height="14" viewBox="0 0 21 21" fill="none">
                <rect width="10" height="10" fill="#f25022" />
                <rect x="11" width="10" height="10" fill="#7fba00" />
                <rect y="11" width="10" height="10" fill="#00a4ef" />
                <rect x="11" y="11" width="10" height="10" fill="#ffb900" />
              </svg>
              <span class="azure-status-text">{{
                azureUserEmail || "Azure Connected"
              }}</span>
              <button
                class="azure-disconnect-btn"
                @click="disconnectAzure"
                title="Disconnect Azure"
              >
                <svg
                  width="12"
                  height="12"
                  viewBox="0 0 24 24"
                  fill="none"
                  stroke="currentColor"
                  stroke-width="2"
                  stroke-linecap="round"
                  stroke-linejoin="round"
                >
                  <line x1="18" y1="6" x2="6" y2="18" />
                  <line x1="6" y1="6" x2="18" y2="18" />
                </svg>
              </button>
            </div>
          </div>

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
              <span class="sidebar-user-name">{{
                user.name || user.login
              }}</span>
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
        </template>
      </div>
    </aside>

    <!-- Center: chat area -->
    <div class="chat-main">
      <!-- Messages -->
      <div class="messages" ref="messagesEl">
        <div class="messages-inner">
          <div v-if="messages.length === 0" class="empty-state">
            <h2 class="es-headline">Azure FinOps Agent</h2>
            <p v-if="!user" class="es-sub">
              Sign in with GitHub to start chatting. Connect Azure for cost
              &amp; billing data.
            </p>
            <p v-else class="es-sub">
              Ask about Azure pricing, compare costs across regions and SKUs, or
              check service health.
            </p>

            <div v-if="user" class="es-prompts">
              <button
                class="es-prompt"
                @click="
                  sendPrompt(
                    'Show a world map of Azure regions color-coded by D4s_v5 VM pricing.',
                  )
                "
              >
                Regional cost map
              </button>
              <button
                class="es-prompt"
                @click="
                  sendPrompt(
                    'Compare D4s_v5, D8s_v5, D16s_v5 monthly costs across East US, West Europe, Southeast Asia.',
                  )
                "
              >
                VM right-sizing
              </button>
              <button
                class="es-prompt"
                @click="
                  sendPrompt(
                    'Compare NC, ND, NV GPU VM pricing in East US. Best price per GPU?',
                  )
                "
              >
                GPU pricing
              </button>
              <button
                class="es-prompt"
                @click="
                  sendPrompt('Any active Azure service health incidents?')
                "
              >
                Service health
              </button>
              <button
                v-if="azureConnected"
                class="es-prompt es-prompt--azure"
                @click="
                  sendPrompt(
                    'Show my Azure cost for the current month grouped by service. Chart it.',
                  )
                "
              >
                My costs this month
              </button>
              <button
                v-if="azureConnected"
                class="es-prompt es-prompt--azure"
                @click="
                  sendPrompt(
                    'What cost optimization recommendations does Azure Advisor have for me?',
                  )
                "
              >
                Advisor savings
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
            <div v-if="msg.role === 'user'" class="bubble bubble--user">
              {{ msg.content }}
            </div>
            <div v-else class="ai-row">
              <div class="ai-avatar">AI</div>
              <div class="ai-content">
                <div
                  v-for="(chart, ci) in msg.charts || []"
                  :key="'chart-' + i + '-' + ci"
                  class="chart-container"
                  :ref="(el) => el && mountChart(el, chart)"
                ></div>
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
      </div>

      <!-- Input bar -->
      <div class="input-area">
        <div
          class="input-wrapper"
          :class="{ 'input-wrapper--disabled': !user }"
        >
          <input
            ref="inputEl"
            v-model="input"
            type="text"
            @keydown.enter.prevent="send"
            :placeholder="
              user
                ? 'Ask about Azure pricing, cost comparisons, or FinOps insights...'
                : 'Sign in with GitHub to start...'
            "
            class="input-field"
            :disabled="!user"
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

    <!-- Right sidebar: Tool calls -->
    <aside
      class="tools-sidebar"
      :class="{ 'tools-sidebar--open': allToolCalls.length > 0 }"
    >
      <div class="tools-sidebar-header">
        <span class="tools-sidebar-title">Tools</span>
        <span class="st-count">{{ allToolCalls.length }}</span>
      </div>
      <div class="tools-sidebar-scroll">
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
          <svg
            v-else-if="tc.success"
            class="st-icon st-icon--ok"
            viewBox="0 0 16 16"
            fill="none"
          >
            <circle cx="8" cy="8" r="7" stroke="#1a7f37" stroke-width="1.5" />
            <path
              d="M5 8.2 7 10.2 11 6"
              stroke="#1a7f37"
              stroke-width="1.5"
              stroke-linecap="round"
              stroke-linejoin="round"
            />
          </svg>
          <svg
            v-else
            class="st-icon st-icon--fail"
            viewBox="0 0 16 16"
            fill="none"
          >
            <circle cx="8" cy="8" r="7" stroke="#cf222e" stroke-width="1.5" />
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
    </aside>

    <!-- Tool popover -->
    <Teleport to="body">
      <div v-if="hoveredTool" class="tool-popover" @click.stop>
        <div class="tool-popover-header">
          <span class="tool-popover-name">{{ hoveredTool.tool }}</span>
          <span class="tool-popover-time">{{
            formatDuration(hoveredTool.durationMs)
          }}</span>
          <button class="tool-popover-close" @click="hoveredTool = null">
            &times;
          </button>
        </div>
        <div v-if="hoveredTool.args" class="tool-popover-section">
          <div class="tool-popover-label">INPUT</div>
          <pre class="tool-popover-pre">{{ formatJson(hoveredTool.args) }}</pre>
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

    <!-- Build badge -->
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
  user: { type: Object, default: null },
});
const emit = defineEmits(["logout", "login"]);

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
let intentAnimTimer = null;
const hoveredTool = ref(null);
const collapsedSections = reactive({
  pricing: true,
  tenant: true,
  finops: false,
  connected: true,
  subs: true,
});
function toggleSection(key) {
  collapsedSections[key] = !collapsedSections[key];
}
const buildSha = ref("");
const buildNumber = ref("0");
const availableModels = ref(["claude-sonnet-4.6"]);
const selectedModel = ref("claude-sonnet-4.6");

// Azure connection state
const azureConnected = ref(false);
const azureUserEmail = ref("");
const azureSubscriptions = ref([]);
const azureManagementGroups = ref([]);
const azureApis = ref([]);
const publicSources = ["Azure Retail Prices", "Azure Service Health"];

async function checkAzureStatus() {
  try {
    const r = await fetch("/auth/azure/status");
    if (r.ok) {
      const data = await r.json();
      azureConnected.value = data.connected;
      if (data.connected) {
        azureUserEmail.value = data.user?.email || data.user?.name || "";
        azureSubscriptions.value = data.subscriptions || [];
        azureManagementGroups.value = data.managementGroups || [];
        azureApis.value = data.apis || [];
      }
    }
  } catch {}
}

async function disconnectAzure() {
  try {
    await fetch("/auth/azure/disconnect", { method: "POST" });
    azureConnected.value = false;
    azureUserEmail.value = "";
    azureSubscriptions.value = [];
    azureManagementGroups.value = [];
    azureApis.value = [];
    await clearMessages();
  } catch {}
}

function dismissPopover() {
  hoveredTool.value = null;
}

async function fetchModels() {
  try {
    const r = await fetch("/api/models");
    if (r.ok) {
      const models = await r.json();
      if (Array.isArray(models) && models.length > 0) {
        const ids = models.map((m) => m.id || m.name || m).filter(Boolean);
        availableModels.value = ids;
        const opusModels = ids.filter((id) => /opus/i.test(id));
        if (opusModels.length > 0) {
          opusModels.sort();
          selectedModel.value = opusModels[opusModels.length - 1];
        }
      }
    }
  } catch {}
}

// Handle user login/logout
watch(
  () => props.user,
  (newUser, oldUser) => {
    if (newUser && !oldUser) {
      // User just logged in — fetch models and Azure status
      fetchModels();
      checkAzureStatus();
    } else if (!newUser && oldUser) {
      // User logged out — clear all UI state
      clearMessages();
      azureConnected.value = false;
      azureUserEmail.value = "";
      azureSubscriptions.value = [];
      azureManagementGroups.value = [];
      azureApis.value = [];
      input.value = "";
      streaming.value = false;
      if (abortController) {
        abortController.abort();
        abortController = null;
      }
    }
  },
);

onMounted(async () => {
  document.addEventListener("click", dismissPopover);
  // Handle Azure OAuth error redirect
  const params = new URLSearchParams(window.location.search);
  const azureError = params.get("azure_error");
  if (azureError) {
    messages.value.push({
      role: "assistant",
      content: `**Azure connection failed:** ${azureError}. Please try again.`,
    });
    window.history.replaceState({}, "", window.location.pathname);
  }
  try {
    const r = await fetch("/api/version");
    if (r.ok) {
      const v = await r.json();
      buildSha.value = v.sha || "";
      buildNumber.value = v.build || "0";
    }
  } catch {}
  // Fetch available models and Azure status in parallel
  if (props.user) {
    await Promise.all([fetchModels(), checkAzureStatus()]);
  }
});

let abortController = null;

async function clearMessages() {
  messages.value = [];
  streamBuffer.value = "";
  streamToolCalls.value = [];
  streamCharts.value = [];
  activeTools.value = [];
  chartInstances.forEach((c) => {
    try {
      c.dispose();
    } catch {}
  });
  chartInstances.length = 0;
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

// ── Prompt categories ──
const pricingPrompts = [
  {
    label: "Regional cost map",
    prompt:
      "Show a world map of Azure regions color-coded by D4s_v5 VM pricing.",
  },
  {
    label: "VM right-sizing",
    prompt:
      "Compare D4s_v5, D8s_v5, D16s_v5 monthly costs across East US, West Europe, Southeast Asia. Show a chart.",
  },
  {
    label: "GPU pricing",
    prompt: "Compare NC, ND, NV GPU VM pricing in East US. Best price per GPU?",
  },
  {
    label: "Database pricing",
    prompt:
      "Compare Cosmos DB serverless vs Azure SQL General Purpose for 10M req/day.",
  },
  {
    label: "Service health",
    prompt:
      "Any active Azure service health incidents in East US, West Europe, Southeast Asia?",
  },
];

const tenantPrompts = [
  {
    label: "My subscriptions",
    prompt:
      "List all my Azure subscriptions with their states and subscription IDs.",
  },
  {
    label: "Resource inventory",
    prompt:
      "Query Resource Graph for a count of all resources by type across my subscriptions.",
  },
  {
    label: "Tag coverage",
    prompt:
      "What percentage of my resources have cost-center and environment tags?",
  },
  {
    label: "Billing accounts",
    prompt:
      "Show my billing account structure — accounts, profiles, invoice sections.",
  },
  {
    label: "Management groups",
    prompt: "Show my management group hierarchy with subscriptions under each.",
  },
];

const finopsPrompts = [
  {
    label: "Cost this month",
    prompt:
      "Show my Azure cost for the current month grouped by service. Chart it.",
  },
  {
    label: "Cost trend",
    prompt: "Show my daily Azure spend for the last 30 days as a line chart.",
  },
  {
    label: "Advisor savings",
    prompt:
      "What cost optimization recommendations does Azure Advisor have for me?",
  },
  {
    label: "Idle resources",
    prompt:
      "Find idle or underutilized VMs, disks, and public IPs across my subscriptions.",
  },
  {
    label: "Reservation usage",
    prompt: "Show my reservation and savings plan utilization rates.",
  },
  {
    label: "Cost by resource group",
    prompt:
      "Break down my current month's Azure cost by resource group and show a bar chart.",
  },
  {
    label: "Month-over-month",
    prompt:
      "Compare this month's Azure spend to last month by service. Highlight the biggest increases.",
  },
  {
    label: "Anomaly detection",
    prompt:
      "Identify any cost anomalies or unexpected spikes in my Azure spend over the past 14 days.",
  },
  {
    label: "Unattached disks",
    prompt:
      "Find all unattached managed disks across my subscriptions and show how much they cost.",
  },
  {
    label: "Top 10 resources",
    prompt:
      "What are my top 10 most expensive Azure resources this month? Show a chart.",
  },
  {
    label: "Budget vs actual",
    prompt:
      "Show my Azure budget vs actual spend for the current billing period.",
  },
  {
    label: "Dev/test savings",
    prompt:
      "Identify resources in dev/test environments that could be shut down or scaled down to save costs.",
  },
  {
    label: "License optimization",
    prompt:
      "Analyze my Microsoft 365 and Azure license usage. Are there unused or underutilized licenses?",
  },
  {
    label: "Storage optimization",
    prompt:
      "Find storage accounts with no recent access and recommend tiering or cleanup for cost savings.",
  },
  {
    label: "Cost forecast",
    prompt:
      "Based on my current spending trend, forecast my Azure bill for the rest of this month.",
  },
];

function sendQuestion(q) {
  if (streaming.value || !props.user) return;
  input.value = q;
  send();
}

// ── ECharts rendering ──

// Country name aliases → Natural Earth canonical names used by world-atlas GeoJSON.
// The LLM may produce short/common names; this map normalizes them so ECharts can match features.
const COUNTRY_NAME_ALIASES = {
  "United States": "United States of America",
  US: "United States of America",
  USA: "United States of America",
  "U.S.": "United States of America",
  "U.S.A.": "United States of America",
  Russia: "Russia",
  "South Korea": "South Korea",
  Korea: "South Korea",
  "Republic of Korea": "South Korea",
  "North Korea": "North Korea",
  "Dem. Rep. Korea": "North Korea",
  DPRK: "North Korea",
  "Czech Republic": "Czechia",
  "DR Congo": "Dem. Rep. Congo",
  "Democratic Republic of the Congo": "Dem. Rep. Congo",
  "Congo (DRC)": "Dem. Rep. Congo",
  "Republic of the Congo": "Congo",
  Tanzania: "United Republic of Tanzania",
  "United Republic of Tanzania": "United Republic of Tanzania",
  "Ivory Coast": "Côte d'Ivoire",
  "Cote d'Ivoire": "Côte d'Ivoire",
  Bosnia: "Bosnia and Herzegovina",
  "Bosnia & Herzegovina": "Bosnia and Herzegovina",
  UAE: "United Arab Emirates",
  UK: "United Kingdom",
  Britain: "United Kingdom",
  "Great Britain": "United Kingdom",
  "Dominican Rep.": "Dominican Republic",
  "Central African Rep.": "Central African Republic",
  "Eq. Guinea": "Equatorial Guinea",
  eSwatini: "eSwatini",
  Swaziland: "eSwatini",
  "East Timor": "Timor-Leste",
  Burma: "Myanmar",
  Laos: "Lao PDR",
  Vatican: "Vatican City",
  Palestine: "Palestine",
  "Falkland Islands": "Falkland Islands",
  Macedonia: "North Macedonia",
  FYROM: "North Macedonia",
};

function normalizeCountryName(name) {
  return COUNTRY_NAME_ALIASES[name] || name;
}

// Normalize all data items in map-type series
function normalizeMapSeriesData(opts) {
  if (!opts || !opts.series) return opts;
  const series = Array.isArray(opts.series) ? opts.series : [opts.series];
  for (const s of series) {
    if (s.type === "map" && Array.isArray(s.data)) {
      s.data = s.data.map((d) => ({
        ...d,
        name: normalizeCountryName(d.name),
      }));
    }
  }
  return opts;
}

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
      // Normalize country names in map series so they match the GeoJSON feature names
      normalizeMapSeriesData(opts);
      // Force white/light map styling to match page background
      applyMapDefaults(opts);
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

function applyMapDefaults(opts) {
  if (!opts) return;
  const lightArea = { areaColor: "#f0f0f0", borderColor: "#ccc" };
  // Map series
  if (opts.series) {
    const series = Array.isArray(opts.series) ? opts.series : [opts.series];
    for (const s of series) {
      if (s.type === "map") {
        s.itemStyle = { ...lightArea, ...(s.itemStyle || {}) };
      }
    }
  }
  // Geo config
  if (opts.geo) {
    const geos = Array.isArray(opts.geo) ? opts.geo : [opts.geo];
    for (const g of geos) {
      g.itemStyle = { ...lightArea, ...(g.itemStyle || {}) };
    }
  }
  // Transparent background so page white shows through
  if (!opts.backgroundColor) {
    opts.backgroundColor = "transparent";
  }
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
      if (isMap) {
        el.style.height = "520px";
      }
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

// -- Connected sources --
// (moved to reactive state: publicSources, azureApis)

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
  if (!props.user) return;
  input.value = text;
  send();
}

async function send() {
  if (!props.user) return;
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
  let hasDeltas = false;
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
              if (intentAnimTimer) {
                clearInterval(intentAnimTimer);
                intentAnimTimer = null;
              }
              streamBuffer.value += data.content;
              hasDeltas = true;
              break;
            case "message":
              // The SDK sends a complete message after streaming deltas.
              // Only use it if no deltas were received (e.g. non-streamed
              // response after tool calls), otherwise it duplicates content.
              if (data.content && !hasDeltas) {
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
              // Animate intent text letter-by-letter
              if (data.tool === "report_intent" && data.args) {
                try {
                  const parsed =
                    typeof data.args === "string"
                      ? JSON.parse(data.args)
                      : data.args;
                  const intentText = parsed.intent;
                  if (intentText) {
                    clearInterval(intentAnimTimer);
                    const prefix = streamBuffer.value;
                    let i = 0;
                    streamBuffer.value = prefix + "*" + intentText.charAt(0);
                    i = 1;
                    intentAnimTimer = setInterval(() => {
                      if (i < intentText.length) {
                        streamBuffer.value =
                          prefix + "*" + intentText.slice(0, i + 1);
                        i++;
                      } else {
                        streamBuffer.value =
                          prefix + "*" + intentText + "…*\n\n";
                        clearInterval(intentAnimTimer);
                        intentAnimTimer = null;
                      }
                    }, 25);
                  }
                } catch {}
              }
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
    clearInterval(intentAnimTimer);
    intentAnimTimer = null;
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
.sidebar-category--border {
  margin-top: 6px;
  border-top: 1px solid var(--border);
  padding-top: 10px;
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
.sidebar-question {
  display: flex;
  align-items: center;
  gap: 10px;
  width: 100%;
  padding: 7px 10px;
  border: none;
  border-radius: 8px;
  background: transparent;
  font-size: 13px;
  font-weight: 400;
  color: var(--text);
  cursor: pointer;
  text-align: left;
  line-height: 1.4;
  transition: all 0.15s;
  font-family: inherit;
}
.sidebar-question:hover {
  background: rgba(9, 105, 218, 0.06);
  color: var(--accent);
}
.sidebar-question:disabled {
  opacity: 0.4;
  cursor: default;
}
.sidebar-question--locked {
  opacity: 0.4;
}
.sidebar-question--locked:hover {
  background: transparent;
  color: var(--text);
}
.sidebar-question-icon {
  flex-shrink: 0;
  width: 22px;
  height: 22px;
  border-radius: 50%;
  font-size: 10px;
  font-weight: 700;
  display: flex;
  align-items: center;
  justify-content: center;
}
.sidebar-question-icon--price {
  background: #ddf4ff;
  color: #0969da;
}
.sidebar-question-icon--tenant {
  background: #fff1e5;
  color: #bc4c00;
}
.sidebar-question-icon--finops {
  background: #dafbe1;
  color: #1a7f37;
}
.sidebar-source {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 5px 10px;
  font-size: 13px;
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
.sidebar-source-dot--azure {
  background: #0078d4;
  box-shadow: 0 0 4px rgba(0, 120, 212, 0.45);
}
.sidebar-source-divider {
  font-size: 10px;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.05em;
  color: var(--text-muted);
  padding: 6px 10px 2px;
}
.sidebar-sub {
  display: flex;
  flex-direction: column;
  padding: 4px 10px;
  gap: 1px;
}
.sidebar-sub-name {
  font-size: 13px;
  color: var(--text);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
.sidebar-sub-id {
  font-size: 10px;
  color: var(--text-muted);
  font-family: monospace;
}
.chat-main {
  flex: 1;
  display: flex;
  flex-direction: column;
  min-width: 0;
  min-height: 0;
  overflow: hidden;
}
.tools-sidebar {
  width: 0;
  flex-shrink: 0;
  border-left: 1px solid var(--border);
  background: var(--surface);
  overflow: hidden;
  transition: width 0.25s cubic-bezier(0.4, 0, 0.2, 1);
  display: flex;
  flex-direction: column;
  font-family: "Cascadia Code", "Fira Code", "Consolas", monospace;
  font-size: 0.75rem;
}
.tools-sidebar--open {
  width: 220px;
}
.tools-sidebar-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 10px 14px;
  border-bottom: 1px solid var(--border);
}
.tools-sidebar-title {
  font-size: 11px;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.06em;
  color: var(--text-muted);
}
.tools-sidebar-scroll {
  flex: 1;
  overflow-y: auto;
  padding: 6px 8px;
  scrollbar-width: thin;
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
}
.st-row:hover {
  background: rgba(9, 105, 218, 0.06);
}
.st-row--clickable {
  cursor: pointer;
}
.st-row--running {
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
}
.tool-popover {
  position: fixed;
  z-index: 9999;
  top: 50%;
  left: 50%;
  transform: translate(-50%, -50%);
  width: 800px;
  max-width: 90vw;
  max-height: 85vh;
  background: #fff;
  border: 1px solid var(--border, #d8dee4);
  border-radius: 10px;
  box-shadow:
    0 8px 30px rgba(0, 0, 0, 0.12),
    0 2px 8px rgba(0, 0, 0, 0.06);
  overflow: hidden;
  display: flex;
  flex-direction: column;
  animation: popover-in 0.15s ease-out;
  font-family: "Cascadia Code", "Fira Code", "Consolas", monospace;
}
@keyframes popover-in {
  from {
    opacity: 0;
    transform: translate(-50%, -50%) scale(0.95);
  }
  to {
    opacity: 1;
    transform: translate(-50%, -50%) scale(1);
  }
}
.tool-popover-header {
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 1rem 1.4rem;
  border-bottom: 1px solid var(--border, #d8dee4);
  background: #fafbfc;
}
.tool-popover-name {
  font-size: 1.1rem;
  font-weight: 600;
  color: #1f2328;
  flex: 1;
}
.tool-popover-time {
  font-size: 0.9rem;
  color: #656d76;
}
.tool-popover-close {
  background: none;
  border: none;
  font-size: 1.4rem;
  color: #656d76;
  cursor: pointer;
  padding: 0 4px;
  line-height: 1;
}
.tool-popover-close:hover {
  color: #1f2328;
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
  font-size: 0.9rem;
  white-space: pre-wrap;
  word-break: break-word;
  max-height: 350px;
  overflow-y: auto;
  line-height: 1.5;
  color: #1f2328;
  scrollbar-width: thin;
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
.es-prompt--azure {
  border-color: #0078d4;
  color: #0078d4;
}
.es-prompt--azure:hover {
  background: rgba(0, 120, 212, 0.06);
}
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
.chart-container {
  width: 100%;
  height: 340px;
  margin: 0 0 0.75rem;
  border: 1px solid var(--border);
  border-radius: 8px;
  background: #fff;
  overflow: hidden;
}
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
.input-wrapper--disabled {
  background: #f9f9f9;
  opacity: 0.6;
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
.login-btn {
  display: flex;
  align-items: center;
  gap: 8px;
  width: 100%;
  padding: 9px 14px;
  border-radius: 8px;
  font-size: 13px;
  font-weight: 500;
  text-decoration: none;
  cursor: pointer;
  transition: all 0.15s;
  justify-content: center;
  border: none;
}
.login-btn--github {
  background: #1f2328;
  color: #fff;
}
.login-btn--github:hover {
  background: #2da44e;
}
.azure-connect-btn {
  display: flex;
  align-items: center;
  gap: 8px;
  width: 100%;
  padding: 8px 14px;
  border-radius: 8px;
  border: 1px solid var(--border);
  background: var(--surface);
  color: var(--text);
  font-size: 13px;
  font-weight: 500;
  text-decoration: none;
  cursor: pointer;
  transition: all 0.15s;
  justify-content: center;
}
.azure-connect-btn:hover {
  border-color: #0078d4;
  color: #0078d4;
  background: rgba(0, 120, 212, 0.04);
}
.azure-status {
  padding: 2px 0;
}
.azure-status-info {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 6px 8px;
  border-radius: 6px;
  background: rgba(0, 120, 212, 0.04);
  border: 1px solid rgba(0, 120, 212, 0.15);
}
.azure-status-text {
  flex: 1;
  font-size: 11px;
  color: var(--text);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
.azure-disconnect-btn {
  background: none;
  border: none;
  color: var(--text-muted);
  cursor: pointer;
  padding: 2px;
  border-radius: 4px;
  display: flex;
  align-items: center;
}
.azure-disconnect-btn:hover {
  color: #cf222e;
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
  margin-bottom: 4px;
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
@media (max-width: 768px) {
  .sidebar {
    display: none;
  }
  .tools-sidebar {
    display: none;
  }
  .tool-popover {
    display: none;
  }
}
</style>
