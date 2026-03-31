<template>
  <div class="chat-view">
    <!-- Azure Portal-style top bar -->
    <header class="portal-header">
      <div class="portal-header-left">
        <button
          class="portal-burger"
          @click="sidebarOpen = !sidebarOpen"
          title="Toggle menu"
        >
          <svg
            width="18"
            height="18"
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            stroke-width="2"
            stroke-linecap="round"
            stroke-linejoin="round"
          >
            <line x1="3" y1="6" x2="21" y2="6" />
            <line x1="3" y1="12" x2="21" y2="12" />
            <line x1="3" y1="18" x2="21" y2="18" />
          </svg>
        </button>
        <span class="portal-title">Azure FinOps Agent</span>
      </div>
      <div class="portal-header-right">
        <div v-if="azureConnected && azureUserEmail" class="portal-user-identity" @click="disconnectAzure" title="Disconnect Azure">
          <div class="portal-user-text">
            <span class="portal-user-email">{{ azureUserEmail }}</span>
            <span class="portal-user-tenant">AZURE TENANT</span>
          </div>
          <div class="portal-user-avatar">
            <svg width="18" height="18" viewBox="0 0 24 24" fill="currentColor">
              <path d="M12 12c2.21 0 4-1.79 4-4s-1.79-4-4-4-4 1.79-4 4 1.79 4 4 4zm0 2c-2.67 0-8 1.34-8 4v2h16v-2c0-2.66-5.33-4-8-4z"/>
            </svg>
          </div>
        </div>
        <div v-else-if="!azureConnected" class="portal-user-identity portal-user-identity--anon">
          <div class="portal-user-avatar">
            <svg width="18" height="18" viewBox="0 0 24 24" fill="currentColor">
              <path d="M12 12c2.21 0 4-1.79 4-4s-1.79-4-4-4-4 1.79-4 4 1.79 4 4 4zm0 2c-2.67 0-8 1.34-8 4v2h16v-2c0-2.66-5.33-4-8-4z"/>
            </svg>
          </div>
        </div>
      </div>
    </header>

    <!-- Auth loading overlay -->
    <div v-if="authLoading" class="auth-overlay">
      <div class="auth-overlay-card">
        <div class="auth-overlay-spinner"></div>
        <p class="auth-overlay-text">Connecting to Azure...</p>
        <p class="auth-overlay-sub">
          {{
            azureConnected
              ? "Adding permissions..."
              : "Authenticating with Microsoft Entra ID..."
          }}
        </p>
      </div>
    </div>

    <!-- Main content area -->
    <div class="portal-body">
      <!-- Left sidebar -->
      <aside class="sidebar" :class="{ 'sidebar--collapsed': !sidebarOpen }">
        <div class="sidebar-scroll">
          <!-- FinOps Prompt Categories -->
          <div
            v-for="cat in visibleCategories"
            :key="cat.key"
            class="sidebar-category"
            :class="{
              'sidebar-category--border': cat !== visibleCategories[0],
            }"
          >
            <div
              class="sidebar-category-label sidebar-category-label--toggle"
              @click="toggleSection(cat.key)"
            >
              <span>{{ cat.label }}</span>
              <svg
                class="collapse-chevron"
                :class="{
                  'collapse-chevron--collapsed': collapsedSections[cat.key],
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
              :class="{
                'collapse-body--collapsed': collapsedSections[cat.key],
              }"
            >
              <button
                v-for="q in cat.prompts"
                :key="q.label"
                class="sidebar-question"
                :disabled="streaming"
                :title="q.prompt"
                @click="sendQuestion(q.prompt)"
              >
                <span
                  class="sidebar-question-icon"
                  :class="'sidebar-question-icon--' + cat.colorClass"
                >
                  {{ cat.icon }}
                </span>
                <span>{{ q.label }}</span>
              </button>
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
                :class="{
                  'collapse-chevron--collapsed': collapsedSections.subs,
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

        <!-- Bottom section -->
        <div class="sidebar-footer">
          <!-- Azure connect/status -->
          <div v-if="!azureConnected" class="azure-connect">
            <button
              class="azure-connect-btn"
              :disabled="authLoading === 'azure'"
              @click="startAuth('azure', '/auth/microsoft')"
            >
              <span
                v-if="authLoading === 'azure'"
                class="auth-spinner auth-spinner--azure"
              ></span>
              <svg
                v-else
                width="16"
                height="16"
                viewBox="0 0 21 21"
                fill="none"
              >
                <rect width="10" height="10" fill="#f25022" />
                <rect x="11" width="10" height="10" fill="#7fba00" />
                <rect y="11" width="10" height="10" fill="#00a4ef" />
                <rect x="11" y="11" width="10" height="10" fill="#ffb900" />
              </svg>
              {{ authLoading === "azure" ? "Connecting..." : "Connect Azure" }}
            </button>
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
                title="Disconnect session (keeps Entra ID consent)"
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
            <!-- Incremental consent: each button navigates to Microsoft Entra ID consent screen -->
            <div class="azure-addons">
              <button
                v-if="!licensesEnabled"
                class="azure-addon-btn"
                @click="startAuth('azure', '/auth/microsoft?tier=licenses')"
                title="Opens Microsoft consent screen for: Read organization info, Read usage reports"
              >
                <span class="azure-addon-icon">+</span>
                License Optimization
              </button>
              <span
                v-else
                class="azure-addon-active"
                title="M365 license inventory + usage reports — consented in Entra ID"
                >✓ Licenses</span
              >

              <button
                v-if="!chargebackEnabled"
                class="azure-addon-btn"
                @click="startAuth('azure', '/auth/microsoft?tier=chargeback')"
                title="Opens Microsoft consent screen for: Read all users' profiles, Read all groups"
              >
                <span class="azure-addon-icon">+</span>
                Cost Allocation
              </button>
              <span
                v-else
                class="azure-addon-active"
                title="User profiles + groups for department chargeback — consented in Entra ID"
                >✓ Chargeback</span
              >

              <button
                v-if="!logAnalyticsEnabled"
                class="azure-addon-btn"
                @click="startAuth('azure', '/auth/microsoft?tier=loganalytics')"
                title="Opens Microsoft consent screen for: Read Log Analytics data"
              >
                <span class="azure-addon-icon">+</span>
                Log Analytics
              </button>
              <span
                v-else
                class="azure-addon-active"
                title="Log Analytics & App Insights KQL — consented in Entra ID"
                >✓ KQL</span
              >
            </div>
            <button
              class="azure-revoke-btn"
              @click="revokeAllPermissions"
              title="Disconnect and revoke all Entra ID permissions for this app"
            >
              Revoke all permissions
            </button>
          </div>
        </div>
      </aside>

      <!-- Center: chat area -->
      <div class="chat-main">
        <!-- Messages -->
        <div class="messages" ref="messagesEl">
          <div class="messages-inner">
            <div v-if="messages.length === 0" class="empty-state">
              <h1 class="es-headline">Azure FinOps Agent</h1>
              <p class="es-sub">
                Analyze costs, forecast spend, optimize resources, and export
                executive-ready PowerPoint decks — all from a single
                conversation.
              </p>

              <div v-if="!azureConnected" class="es-connect-bar">
                <button
                  class="es-step-btn es-step-btn--azure"
                  :disabled="authLoading === 'azure'"
                  @click="startAuth('azure', '/auth/microsoft')"
                >
                  <svg width="14" height="14" viewBox="0 0 21 21" fill="none">
                    <rect
                      width="10"
                      height="10"
                      fill="#fff"
                      fill-opacity="0.9"
                    />
                    <rect
                      x="11"
                      width="10"
                      height="10"
                      fill="#fff"
                      fill-opacity="0.7"
                    />
                    <rect
                      y="11"
                      width="10"
                      height="10"
                      fill="#fff"
                      fill-opacity="0.7"
                    />
                    <rect
                      x="11"
                      y="11"
                      width="10"
                      height="10"
                      fill="#fff"
                      fill-opacity="0.5"
                    />
                  </svg>
                  {{
                    authLoading === "azure"
                      ? "Connecting..."
                      : "Connect Azure tenant for contextual FinOps"
                  }}
                </button>
              </div>

              <!-- Capabilities -->
              <div class="es-capabilities">
                <div class="es-capabilities-grid">
                  <div class="es-cap-item">
                    <div class="es-cap-title">
                      Cost analysis across all scopes
                    </div>
                    <div class="es-cap-desc">
                      Break down spend by service, resource group, tag,
                      subscription, and region — across all subscriptions and
                      management groups at once
                    </div>
                  </div>
                  <div class="es-cap-item">
                    <div class="es-cap-title">20+ interactive chart types</div>
                    <div class="es-cap-desc">
                      Bar, line, pie, scatter, world maps, treemaps, heatmaps,
                      radar, gauge, funnel, sankey — rendered inline, not static
                      screenshots
                    </div>
                  </div>
                  <div class="es-cap-item">
                    <div class="es-cap-title">Python data processing</div>
                    <div class="es-cap-desc">
                      Chains multiple API calls and runs Python with pandas and
                      numpy for complex calculations and transformations
                    </div>
                  </div>
                  <div class="es-cap-item">
                    <div class="es-cap-title">PowerPoint export</div>
                    <div class="es-cap-desc">
                      Generates .pptx presentations with embedded charts and
                      data tables — walk into a stakeholder meeting ready
                    </div>
                  </div>
                  <div class="es-cap-item">
                    <div class="es-cap-title">M365 license and Copilot ROI</div>
                    <div class="es-cap-desc">
                      Queries Microsoft Graph for unused licenses, Copilot seat
                      usage, and license waste across your tenant
                    </div>
                  </div>
                  <div class="es-cap-item">
                    <div class="es-cap-title">KQL on Log Analytics</div>
                    <div class="es-cap-desc">
                      Runs queries for ingestion costs, VM performance,
                      container insights, and activity audit telemetry
                    </div>
                  </div>
                  <div class="es-cap-item">
                    <div class="es-cap-title">Every Azure resource type</div>
                    <div class="es-cap-desc">
                      Reservations, savings plans, Cosmos DB, AKS, Databricks,
                      Redis, Synapse, ML, Carbon, and 30+ more
                    </div>
                  </div>
                  <div class="es-cap-item">
                    <div class="es-cap-title">Chargeback and tag audit</div>
                    <div class="es-cap-desc">
                      Auto-generates chargeback reports by tag or owner and
                      quantifies untagged cost across your environment
                    </div>
                  </div>
                </div>
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
                <div class="ai-header">
                  <div class="ai-avatar">AI</div>
                </div>
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
                  <div v-if="msg.followUp" class="follow-up-buttons">
                    <button
                      class="follow-up-btn"
                      @click="sendQuestion(msg.followUp.prompt)"
                    >
                      {{ msg.followUp.label }}
                    </button>
                  </div>
                  <div v-if="msg.pptx" class="pptx-inline-download">
                    <a
                      :href="'/api/download/pptx/' + msg.pptx.fileId"
                      class="pptx-download-btn"
                      download
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
                        <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4" />
                        <polyline points="7 10 12 15 17 10" />
                        <line x1="12" y1="15" x2="12" y2="3" />
                      </svg>
                      Download {{ msg.pptx.fileName }} ({{
                        msg.pptx.slideCount
                      }}
                      slides)
                    </a>
                  </div>
                </div>
              </div>
            </div>

            <!-- Streaming indicator -->
            <div v-if="streaming" class="message-row message-row--ai">
              <div class="ai-row">
                <div class="ai-header">
                  <div class="ai-avatar">AI</div>
                  <span v-if="streamIntent" class="stream-intent">
                    {{ streamIntent }}
                  </span>
                </div>
                <div class="ai-content">
                  <div
                    v-for="(chart, ci) in streamCharts"
                    :key="'stream-chart-' + ci"
                    class="chart-container"
                    :ref="(el) => el && mountChart(el, chart)"
                  ></div>
                  <div class="message-text" v-if="streamBuffer">
                    <span v-html="renderContent(streamBuffer)"></span>
                    <span class="streaming-cursor"></span>
                  </div>
                  <div class="message-text" v-else-if="!streamIntent">
                    <span class="streaming-cursor"></span>
                  </div>
                </div>
              </div>
            </div>
          </div>
        </div>

        <!-- PPTX download (streaming) -->
        <div v-if="pptxReady" class="pptx-download-bar">
          <a
            :href="'/api/download/pptx/' + pptxReady.fileId"
            class="pptx-download-btn"
            download
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
              <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4" />
              <polyline points="7 10 12 15 17 10" />
              <line x1="12" y1="15" x2="12" y2="3" />
            </svg>
            Download {{ pptxReady.fileName }} ({{ pptxReady.slideCount }}
            slides)
          </a>
        </div>

        <!-- Mobile auth bar (hidden on desktop, shown on mobile) -->
        <div class="mobile-auth-bar"></div>

        <!-- Input bar -->
        <div class="input-area">
          <div
            class="input-wrapper"
            :class="{ 'input-wrapper--disabled': false }"
          >
            <input
              ref="inputEl"
              v-model="input"
              type="text"
              @keydown.enter.prevent="send"
              :placeholder="
                user
                  ? 'Ask about Azure pricing, cost comparisons, or FinOps insights...'
                  : 'Ask about Azure costs, pricing, or optimization...'
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
          <div class="input-side-actions">
            <button
              class="input-pill-btn"
              :class="{
                'input-pill-btn--disabled': messages.length === 0 || streaming,
              }"
              :disabled="messages.length === 0 || streaming"
              @click="clearMessages"
              title="Clear chat"
            >
              <svg
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
              <span class="input-pill-label">Clear</span>
            </button>
            <button
              class="input-pill-btn"
              :class="{
                'input-pill-btn--disabled': messages.length < 2 || streaming,
              }"
              :disabled="messages.length < 2 || streaming"
              @click="requestPresentation"
              title="Generate PowerPoint"
            >
              <svg
                width="14"
                height="14"
                viewBox="0 0 24 24"
                fill="none"
                stroke="currentColor"
                stroke-width="2"
                stroke-linecap="round"
                stroke-linejoin="round"
              >
                <path
                  d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"
                />
                <polyline points="14 2 14 8 20 8" />
                <path d="M9 13h2v4h-2z" />
                <path d="M9 11h4" />
              </svg>
              <span class="input-pill-label">PowerPoint</span>
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
    </div>
    <!-- end portal-body -->

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
    <div class="build-badge">B{{ buildNumber }}</div>
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
const streamFollowUp = ref(null);
const streamIntent = ref("");
const pptxReady = ref(null);
const pptxDownloads = ref([]);
const messagesEl = ref(null);
const inputEl = ref(null);
const chartInstances = [];
let intentAnimTimer = null;

// Smooth text reveal — drains pending chars fast enough to never lag behind the LLM
let pendingText = "";
let textAnimFrame = null;

function enqueueText(text) {
  pendingText += text;
  if (!textAnimFrame) drainText();
}

function drainText() {
  if (pendingText.length === 0) {
    textAnimFrame = null;
    return;
  }
  // Reveal ~10 chars per frame (60fps = ~600 chars/sec — faster than any LLM)
  const batch = Math.min(10, pendingText.length);
  streamBuffer.value += pendingText.slice(0, batch);
  pendingText = pendingText.slice(batch);
  textAnimFrame = requestAnimationFrame(drainText);
}

function flushText() {
  if (textAnimFrame) {
    cancelAnimationFrame(textAnimFrame);
    textAnimFrame = null;
  }
  if (pendingText.length > 0) {
    streamBuffer.value += pendingText;
    pendingText = "";
  }
}

const hoveredTool = ref(null);
const collapsedSections = reactive({
  subs: true,
  finops_cost: true,
  finops_optimize: true,
  finops_reservations: true,
  finops_storage: true,
  finops_licensing: true,
  finops_governance: true,
  finops_pricing: false,
  finops_infra: true,
  finops_ai_data: true,
  finops_advanced: true,
  finops_pricing_public: false,
});
function toggleSection(key) {
  collapsedSections[key] = !collapsedSections[key];
}
const buildSha = ref("");
const buildNumber = ref("0");
const sidebarOpen = ref(true);
const availableModels = ref(["claude-sonnet-4.6"]);
const selectedModel = ref("claude-sonnet-4.6");

// Auth loading state
const authLoading = ref(""); // "" | "github" | "azure"

// Azure connection state
const azureConnected = ref(false);
const azureUserEmail = ref("");
const azureSubscriptions = ref([]);
const azureManagementGroups = ref([]);
const azureApis = ref([]);
const graphEnabled = ref(false);
const licensesEnabled = ref(false);
const chargebackEnabled = ref(false);
const logAnalyticsEnabled = ref(false);

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
        graphEnabled.value = data.graphEnabled || false;
        const gt = data.graphTier || "";
        licensesEnabled.value = gt.includes("licenses");
        chargebackEnabled.value = gt.includes("chargeback");
        logAnalyticsEnabled.value = data.logAnalyticsEnabled || false;
      }
    }
  } catch {}
}

function startAuth(provider, url) {
  authLoading.value = provider;
  sessionStorage.setItem("authLoading", provider);
  setTimeout(() => {
    window.location.href = url;
  }, 100);
}

async function disconnectAzure() {
  try {
    await fetch("/auth/azure/disconnect", { method: "POST" });
    azureConnected.value = false;
    azureUserEmail.value = "";
    azureSubscriptions.value = [];
    azureManagementGroups.value = [];
    azureApis.value = [];
    graphEnabled.value = false;
    licensesEnabled.value = false;
    chargebackEnabled.value = false;
    logAnalyticsEnabled.value = false;
    await clearMessages();
  } catch {}
}

async function revokeAllPermissions() {
  if (
    !confirm(
      "This will disconnect your session and clear all tokens. When you reconnect, you'll see the consent screen again.\n\nTo fully revoke app permissions in Entra ID, visit: https://myapps.microsoft.com\n\nContinue?",
    )
  )
    return;
  try {
    // Server-side revoke removes consent grants from Entra ID
    await fetch("/auth/azure/revoke", { method: "POST" });
    azureConnected.value = false;
    azureUserEmail.value = "";
    azureSubscriptions.value = [];
    azureManagementGroups.value = [];
    azureApis.value = [];
    graphEnabled.value = false;
    licensesEnabled.value = false;
    chargebackEnabled.value = false;
    logAnalyticsEnabled.value = false;
    await clearMessages();
  } catch {}
}

// When Azure connects, expand Cost Analysis and collapse the public categories
watch(azureConnected, (connected, wasConnected) => {
  if (!connected) return;
  collapsedSections.finops_cost = false;
  collapsedSections.finops_pricing = true;
  collapsedSections.finops_pricing_public = true;
  // Auto-clear chat when Azure connects — removes stale "Connect Azure first" messages
  // and resets the Copilot session so the LLM knows the user is now connected
  if (!wasConnected) clearMessages();
});

// Reset Copilot session when addon tiers are enabled so LLM picks up new tokens
watch(graphEnabled, (enabled, was) => {
  if (enabled && !was) clearMessages();
});
watch(logAnalyticsEnabled, (enabled, was) => {
  if (enabled && !was) clearMessages();
});

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
  // Restore auth loading state after OAuth redirect (page reload clears ref)
  const pendingAuth = sessionStorage.getItem("authLoading");
  if (pendingAuth) {
    authLoading.value = pendingAuth;
  }
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
  // Clear auth loading overlay now that status is resolved
  if (pendingAuth) {
    sessionStorage.removeItem("authLoading");
    authLoading.value = "";
  }
});

let abortController = null;

async function clearMessages() {
  // Abort any in-flight request first
  if (abortController) {
    abortController.abort();
    abortController = null;
  }
  streaming.value = false;
  messages.value = [];
  streamBuffer.value = "";
  streamToolCalls.value = [];
  streamCharts.value = [];
  pptxReady.value = null;
  pptxDownloads.value = [];
  activeTools.value = [];
  hoveredTool.value = null;
  input.value = "";
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
// Show all categories when Azure is connected; only public ones otherwise
const visibleCategories = computed(() =>
  azureConnected.value
    ? finopsCategories
    : finopsCategories.filter((c) => !c.requiresAzure),
);

const finopsCategories = [
  // ── 1. Advanced FinOps — lead with maturity, anomalies, unit economics (impressive) ──
  {
    key: "finops_advanced",
    label: "Advanced FinOps",
    icon: "F",
    colorClass: "cat-advanced",
    requiresAzure: true,
    prompts: [
      {
        label: "FinOps maturity assessment",
        prompt:
          "Conduct a structured FinOps maturity assessment. Check each dimension: (1) Do I have budgets set per subscription? (2) What % of resources are tagged? (3) Am I using reservations/savings plans? (4) Advisor recommendation adoption rate? (5) Do I have cost exports configured? (6) Are my management groups structured for cost governance? Score each as Crawl/Walk/Run and recommend next steps.",
      },
      {
        label: "Cost anomaly detection",
        prompt:
          "Check my Azure Cost Management anomaly alerts. Show all active cost alerts and anomalies detected in the past 30 days. For each, show the affected scope, expected vs actual cost, deviation percentage, and the root cause if identified.",
      },
      {
        label: "Unit economics",
        prompt:
          "Help me calculate unit economics for my top workloads. For each of my top 5 resource groups by cost, calculate the cost-per-day and trend over the past 30 days. If I share transaction or user counts, we can derive cost-per-unit KPIs.",
      },
      {
        label: "Cost allocation model",
        prompt:
          "Analyze my cost allocation strategy. Show costs broken down by subscription, resource group, and tags (cost-center, environment, owner). What percentage of my spend can be attributed vs unattributed? Recommend improvements to my allocation model.",
      },
      {
        label: "Cross-sub benchmarking",
        prompt:
          "Benchmark cost efficiency across all my subscriptions. For each subscription, calculate: total cost, resource count, cost-per-resource, cost-per-vCPU, and month-over-month change. Rank by cost efficiency and identify outliers.",
      },
      {
        label: "Showback report",
        prompt:
          "Generate a showback report for the current month — show each department/team their Azure costs by tag (cost-center or owner) without billing attribution. Include a summary table and a pie chart of cost distribution across teams.",
      },
      {
        label: "FOCUS cost mapping",
        prompt:
          "Show my current month's costs mapped to FOCUS (FinOps Open Cost & Usage Specification) concepts: BilledCost (actual invoice amount), EffectiveCost (amortized with commitments spread), and ChargeCategory (usage, purchase, tax, credit). Show a comparison table.",
      },
      {
        label: "Estimate new deployment",
        prompt:
          "I want to estimate the monthly cost of a new deployment. Help me price out the infrastructure — I'll describe the resources I need (VMs, storage, databases, networking) and you calculate the estimated monthly cost using Azure retail pricing.",
      },
    ],
  },
  // ── 2. Cost Analysis — "Where is my money going?" ──
  {
    key: "finops_cost",
    label: "Cost Analysis",
    icon: "$",
    colorClass: "cat-cost",
    requiresAzure: true,
    prompts: [
      {
        label: "Cost this month",
        prompt:
          "Show my Azure cost for the current month grouped by service. Chart it.",
      },
      {
        label: "Cost trend (30 days)",
        prompt:
          "Show my daily Azure spend for the last 30 days as a line chart.",
      },
      {
        label: "Cost forecast",
        prompt:
          "Based on my current spending trend, forecast my Azure bill for the rest of this month. Show the projected cost vs budget as a line chart.",
      },
      {
        label: "Top 10 costly resources",
        prompt:
          "What are my top 10 most expensive Azure resources this month? Show a chart.",
      },
      {
        label: "Month-over-month change",
        prompt:
          "Compare this month's Azure spend to last month by service. Highlight the biggest increases and show a chart.",
      },
      {
        label: "Cost by subscription",
        prompt:
          "Compare Azure costs across all my subscriptions for the current month. Show a bar chart ranking subscriptions by spend, and a table with subscription name, cost, and month-over-month change.",
      },
      {
        label: "Budget vs actual",
        prompt:
          "Show my Azure budgets vs actual spend for the current billing period. Which budgets are at risk of being exceeded? Show a gauge chart per budget.",
      },
      {
        label: "Cost by resource group",
        prompt:
          "Break down my current month's Azure cost by resource group and show a bar chart.",
      },
      {
        label: "Cost by tag",
        prompt:
          "Break down my Azure costs by the cost-center tag for the current month. Show a pie chart and table. Which cost centers have the highest spend?",
      },
      {
        label: "Cost by region",
        prompt:
          "Break down my Azure spend by region/location for the current month. Show a bar chart and identify which regions are the most expensive. Are there opportunities to move workloads to cheaper regions?",
      },
      {
        label: "Amortized cost view",
        prompt:
          "Show my amortized Azure costs for the current month — spreading reservation and savings plan purchases across their term. Compare amortized vs actual cost by service in a table.",
      },
    ],
  },
  // ── 3. Optimization — "How can I save money?" ──
  {
    key: "finops_optimize",
    label: "Optimization",
    icon: "O",
    colorClass: "cat-optimize",
    requiresAzure: true,
    prompts: [
      {
        label: "Advisor recommendations",
        prompt:
          "What cost optimization recommendations does Azure Advisor have for me? Group them by impact (high, medium, low) and show the estimated annual savings for each.",
      },
      {
        label: "Idle resources cleanup",
        prompt:
          "Find all idle or underutilized VMs, disks, public IPs, App Service plans, and load balancers across my subscriptions. For each, show the resource name, type, resource group, monthly cost, and tags in a table sorted by cost. What's my total potential savings?",
      },
      {
        label: "VM right-sizing",
        prompt:
          "Analyze my running VMs and identify which ones are oversized based on Advisor recommendations. For each, show current SKU, recommended SKU, current monthly cost, projected monthly cost, and monthly savings. Sort by highest savings first.",
      },
      {
        label: "Orphaned resources",
        prompt:
          "Find all orphaned resources across my subscriptions — unattached disks, unused public IPs, empty resource groups, NICs not attached to VMs, and NSGs not attached to any subnet or NIC. List them with their monthly cost and tags so I can clean them up.",
      },
      {
        label: "Unattached disks",
        prompt:
          "How many unattached disks do I have and what would be my savings if I remove them? List them in a table ranked by highest savings potential at the top. Include the disk name, size, SKU, monthly cost, resource group, and all tags so I can plan outreach to the responsible teams.",
      },
      {
        label: "Dev/test savings",
        prompt:
          "Identify resources in dev/test environments (by tag or naming convention) that could be shut down, scaled down, or deallocated to save costs. Show a table with the resource, current SKU, recommended action, and estimated monthly savings.",
      },
      {
        label: "Optimize top resource group",
        prompt:
          "I need to cost optimize my most expensive resource group. First show me my top 5 resource groups by cost this month, then give me the top Advisor recommendations and idle resources for the most expensive one.",
      },
      {
        label: "App Service consolidation",
        prompt:
          "List all my App Service plans with their pricing tier, instance count, and the number of apps hosted on each. Identify plans with low utilization or only one app that could be consolidated. Show potential savings from merging underutilized plans.",
      },
      {
        label: "AKS cost optimization",
        prompt:
          "Analyze my AKS clusters. Show each cluster's node pool configuration, VM sizes, node count, and monthly cost. Identify over-provisioned node pools where CPU/memory requests are significantly below capacity. Recommend right-sizing actions with estimated savings.",
      },
      {
        label: "Network cost review",
        prompt:
          "Review my network-related costs: ExpressRoute circuits, VPN gateways, NAT gateways, public IPs, and Application Gateways. List each resource with its SKU, monthly cost, and utilization. Flag any that appear oversized or idle.",
      },
    ],
  },
  // ── 4. Reservations & Commitments ──
  {
    key: "finops_reservations",
    label: "Reservations & Commitments",
    icon: "R",
    colorClass: "cat-reservations",
    requiresAzure: true,
    prompts: [
      {
        label: "Reservation utilization",
        prompt:
          "Are we using our reservations? List the usage/utilization of all my reservations and identify under-utilized ones. Show a table with the reservation name, resource type, utilization %, and the monetary waste from unused capacity. Sort by lowest utilization first.",
      },
      {
        label: "Savings plan coverage",
        prompt:
          "Show my savings plan and reservation coverage for compute. What percentage of my eligible spend is covered by commitments vs pay-as-you-go? Show a pie chart.",
      },
      {
        label: "Reservation recommendations",
        prompt:
          "Based on my usage patterns, what new reservation purchases does Azure recommend? Show the recommended reservations with resource type, term, estimated monthly savings, and upfront cost in a table.",
      },
      {
        label: "Savings plan vs reservation",
        prompt:
          "Based on my compute spend patterns, should I buy a savings plan or a reservation? Compare the savings from each option for my top 5 compute workloads. Show a table with resource, current monthly cost, RI savings, SP savings, and recommendation.",
      },
      {
        label: "Expiring reservations",
        prompt:
          "List all my reservations that expire in the next 90 days. For each, show the reservation name, resource type, expiry date, current utilization, and the monthly cost impact if not renewed.",
      },
      {
        label: "Reserved vs pay-as-you-go",
        prompt:
          "Compare the cost of running 10x D4s_v5 VMs in East US on pay-as-you-go vs 1-year reserved vs 3-year reserved. Show a bar chart with total annual cost and the percentage savings for each option.",
      },
      {
        label: "RI exchange opportunities",
        prompt:
          "Analyze my current reservations for exchange opportunities. Which reservations are underutilized and could be exchanged for a better-fitting SKU or region? Show the current reservation, utilization %, and recommended exchange target.",
      },
    ],
  },
  // ── 5. Governance & Reporting ──
  {
    key: "finops_governance",
    label: "Governance & Reporting",
    icon: "G",
    colorClass: "cat-governance",
    requiresAzure: true,
    prompts: [
      {
        label: "Executive cost summary",
        prompt:
          "Create an executive summary of my Azure spend: total cost this month, month-over-month trend, top 5 services by cost, biggest cost increases, active Advisor savings opportunities, and reservation utilization. Include charts.",
      },
      {
        label: "Chargeback report",
        prompt:
          "Generate a chargeback report for the current month. Break down costs by the owner tag or cost-center tag. Show a bar chart and table with each team's total spend, top services, and month-over-month change.",
      },
      {
        label: "Resource inventory",
        prompt:
          "Query Resource Graph for a count of all resources by type across my subscriptions. Show a pie chart of the top 15 resource types and a table with the full breakdown.",
      },
      {
        label: "My subscriptions",
        prompt:
          "List all my Azure subscriptions with their states and subscription IDs.",
      },
      {
        label: "Policy compliance",
        prompt:
          "Show my Azure Policy compliance state. Which policies have the most non-compliant resources? List the top 10 non-compliant policies with the count of affected resources and their resource types.",
      },
      {
        label: "Carbon emissions",
        prompt:
          "Show my Azure carbon emissions data. Break down emissions by service and region. Which workloads have the highest carbon footprint, and what would be the impact of moving them to a greener region?",
      },
      {
        label: "Billing accounts",
        prompt:
          "Show my billing account structure — accounts, profiles, invoice sections.",
      },
    ],
  },
  // ── 6. Licensing & Hybrid Benefit ──
  {
    key: "finops_licensing",
    label: "Licensing & Hybrid",
    icon: "L",
    colorClass: "cat-licensing",
    requiresAzure: true,
    prompts: [
      {
        label: "Azure Hybrid Benefit check",
        prompt:
          "Which of my Windows VMs and SQL databases are NOT using Azure Hybrid Benefit? List them with their current monthly cost and the savings I'd get by enabling AHUB. Show total potential savings.",
      },
      {
        label: "License optimization",
        prompt:
          "Analyze my Microsoft 365 and Azure license usage. List all license types with total purchased, assigned, and unused counts. Show unused licenses with their monthly cost — what's my total waste from unassigned licenses?",
      },
      {
        label: "M365 Copilot ROI",
        prompt:
          "Show my Microsoft 365 Copilot license usage. How many seats are assigned vs actively used? Which users haven't used Copilot in the last 30 days? Calculate the monthly waste from inactive Copilot licenses.",
      },
      {
        label: "Windows Server licensing",
        prompt:
          "Audit my Windows Server VMs. How many are using Azure Hybrid Benefit vs pay-as-you-go licensing? Show the per-VM cost difference and total savings opportunity from enabling AHUB on all eligible VMs.",
      },
    ],
  },
  // ── 7. Storage & Data ──
  {
    key: "finops_storage",
    label: "Storage & Data",
    icon: "S",
    colorClass: "cat-storage",
    requiresAzure: true,
    prompts: [
      {
        label: "Storage optimization",
        prompt:
          "Find storage accounts with no recent access and recommend tiering or cleanup for cost savings. Show the storage account, current tier, last access date, size, monthly cost, and recommended tier in a table.",
      },
      {
        label: "Blob lifecycle analysis",
        prompt:
          "Analyze my blob storage accounts. Which ones are missing lifecycle management policies? For large storage accounts, estimate the savings if I moved data older than 30 days to Cool, 90 days to Cold, and 180 days to Archive tier.",
      },
      {
        label: "Disk SKU optimization",
        prompt:
          "List all my managed disks with their SKU (Premium, Standard SSD, Standard HDD). Identify Premium SSD disks attached to VMs with low IOPS usage that could be downgraded to Standard SSD. Show the current vs recommended cost per disk.",
      },
      {
        label: "Log Analytics ingestion cost",
        prompt:
          "Analyze my Log Analytics workspace ingestion costs. Show data volume by table (e.g. AzureDiagnostics, Perf, ContainerLog) over the past 30 days. Which tables are the biggest cost drivers? Recommend tables to move to Basic Logs or archive tier.",
      },
      {
        label: "Storage tier pricing",
        prompt:
          "Compare the cost of storing 10 TB in Azure Blob Storage across Hot, Cool, Cold, and Archive tiers in East US. Include per-GB storage cost and per-10K read/write transaction costs in a table.",
      },
      {
        label: "GPv1 to GPv2 migration",
        prompt:
          "I need to upgrade storage from GPv1 to GPv2. List all my storage accounts, identify which are still GPv1, and do a cost analysis comparing current GPv1 costs vs projected GPv2 costs. Show the cost difference per account in a table so I can plan the migration.",
      },
    ],
  },
  // ── 8. AI, GPU & Data Platforms ──
  {
    key: "finops_ai_data",
    label: "AI, GPU & Data",
    icon: "A",
    colorClass: "cat-ai",
    requiresAzure: true,
    prompts: [
      {
        label: "GPU compute inventory",
        prompt:
          "List all my GPU VMs (NC, ND, NV series) across all subscriptions. Show each VM's name, size, GPU count, resource group, monthly cost, and tags. Which GPU VMs have low utilization and could be deallocated or right-sized?",
      },
      {
        label: "Azure ML cost analysis",
        prompt:
          "List all my Azure ML workspaces and their compute resources (instances, clusters, endpoints). Show each compute's type, VM size, state (running/stopped), monthly cost, and idle time. Identify ML computes burning money while idle.",
      },
      {
        label: "Databricks workspace review",
        prompt:
          "List all my Azure Databricks workspaces with their pricing tier (standard/premium), SKU, managed resource group, and monthly cost. Identify workspaces on Premium tier that could use Standard instead.",
      },
      {
        label: "Cosmos DB RU optimization",
        prompt:
          "List all my Cosmos DB accounts with their provisioned throughput (RU/s), autoscale settings, consistency level, and monthly cost. Identify databases that are over-provisioned (low RU utilization) or could benefit from switching to autoscale or serverless.",
      },
      {
        label: "SQL Managed Instance sizing",
        prompt:
          "List all my SQL Managed Instances with their tier, vCores, storage, and monthly cost. Identify instances that are over-provisioned based on Advisor recommendations. Show the potential savings from right-sizing.",
      },
      {
        label: "Synapse & Data Factory costs",
        prompt:
          "List all my Synapse workspaces (dedicated SQL pools, Spark pools) and Data Factory instances. Show each resource's configuration and monthly cost. Identify idle dedicated SQL pools that should be paused and over-provisioned Spark pools.",
      },
      {
        label: "Container Apps review",
        prompt:
          "List all my Container Apps and their environments. Show each app's min/max replicas, CPU/memory allocation, and monthly cost. Identify apps with over-provisioned resources or that could benefit from scale-to-zero.",
      },
      {
        label: "Redis Cache optimization",
        prompt:
          "List all my Azure Redis Cache instances with their tier (Basic/Standard/Premium), cache size, and monthly cost. Identify caches that could be downgraded to a lower tier or smaller size based on usage patterns.",
      },
      {
        label: "Databricks vs ML cost",
        prompt:
          "Compare my Azure Databricks and Azure ML compute costs. List all Databricks workspaces with their pricing tier and managed resource group costs, and all ML workspaces with their compute costs. Which platform is costing more and where are the optimization opportunities?",
      },
    ],
  },
  // ── 9. Infrastructure & Security ──
  {
    key: "finops_infra",
    label: "Infrastructure & Security",
    icon: "I",
    colorClass: "cat-infra",
    requiresAzure: true,
    prompts: [
      {
        label: "Tag compliance audit",
        prompt:
          "Audit tag compliance across all my subscriptions. What percentage of resources have cost-center, environment, and owner tags? List the resource groups with the worst tag coverage so I can follow up with the responsible teams.",
      },
      {
        label: "Security posture",
        prompt:
          "Show my Microsoft Defender for Cloud secure score across all subscriptions. List the top 10 unhealthy security recommendations with their severity and affected resource count.",
      },
      {
        label: "Resource health",
        prompt:
          "Check the health status of all my Azure resources. List any resources currently in a degraded or unavailable state with their resource type, resource group, and impact details.",
      },
      {
        label: "Quota utilization",
        prompt:
          "Check my Azure subscription quota usage for compute (vCPUs), storage, and networking. Which quotas am I closest to hitting? Show a table with quota name, limit, current usage, and usage percentage sorted by highest utilization.",
      },
      {
        label: "RBAC review",
        prompt:
          "List all Owner and Contributor role assignments across my subscriptions. Identify any direct user assignments (not via groups) and any assignments to external/guest accounts — these are security risks.",
      },
      {
        label: "Resource locks audit",
        prompt:
          "List all resource locks (CanNotDelete and ReadOnly) across my subscriptions. Are my critical production resources properly protected? Identify high-value resources that are missing locks.",
      },
      {
        label: "Management groups",
        prompt:
          "Show my management group hierarchy with subscriptions under each.",
      },
    ],
  },
  // ── 10. Pricing & Estimator (public — no login) ──
  {
    key: "finops_pricing",
    label: "Pricing & Estimates",
    icon: "$",
    colorClass: "cat-pricing",
    requiresAzure: false,
    prompts: [
      {
        label: "Compare VM pricing by region",
        prompt:
          "Compare the monthly cost of a D4s_v5 VM across the 10 cheapest Azure regions. Show a bar chart.",
      },
      {
        label: "Spot vs on-demand savings",
        prompt:
          "Compare spot vs on-demand pricing for D4s_v5, D8s_v5, and NC24ads_A100_v4 in East US. Show the discount % for each.",
      },
      {
        label: "Reserved vs pay-as-you-go",
        prompt:
          "Compare pay-as-you-go vs 1-year vs 3-year reserved pricing for a D4s_v5 VM in East US.",
      },
      {
        label: "Storage tier comparison",
        prompt:
          "Compare Azure Blob Storage costs for 10 TB across Hot, Cool, Cold, and Archive tiers in East US.",
      },
      {
        label: "Database pricing comparison",
        prompt:
          "Compare monthly cost of Azure SQL 8-vCore vs Cosmos DB 10K RU/s vs PostgreSQL Flexible 8-vCore with 500 GB storage.",
      },
      {
        label: "3-tier app cost estimate",
        prompt:
          "Estimate monthly cost for a 3-tier app in East US: 2x D4s_v5 VMs, Azure SQL 4-vCore 500 GB, 1 TB Premium SSD, Standard LB.",
      },
      {
        label: "AKS vs Container Apps vs Functions",
        prompt:
          "Compare cost of running 20 microservices on AKS vs Azure Container Apps vs Azure Functions consumption plan.",
      },
      {
        label: "GPU training cluster cost",
        prompt:
          "Compare monthly cost of 4x A100 (ND96asr_v4) vs 4x H100 (NC80adis_H100_v5) on-demand in East US.",
      },
      {
        label: "Global VM pricing map",
        prompt:
          "Show a world map of Azure regions color-coded by D4s_v5 VM pricing.",
      },
      {
        label: "Azure service health",
        prompt:
          "Are there any active Azure service health incidents right now?",
      },
      {
        label: "Kubernetes node pool sizing",
        prompt:
          "Compare monthly cost of an AKS cluster with 3x D4s_v5 vs 3x D8s_v5 vs 3x D16s_v5 nodes in East US.",
      },
      {
        label: "Managed disk pricing",
        prompt:
          "Compare pricing for 1 TB managed disks: Premium SSD v2 vs Premium SSD vs Standard SSD vs Standard HDD in East US.",
      },
      {
        label: "ExpressRoute vs VPN cost",
        prompt:
          "Compare monthly cost of ExpressRoute Standard 1 Gbps vs 2x VPN Gateway VpnGw2AZ for hybrid connectivity.",
      },
      {
        label: "Serverless database cost",
        prompt:
          "Compare Azure SQL Serverless vs Cosmos DB Serverless vs PostgreSQL Flexible for a workload with 1M requests/day and 100 GB storage.",
      },
      {
        label: "Azure OpenAI token pricing",
        prompt:
          "Compare Azure OpenAI pricing for GPT-4o vs GPT-4o-mini vs GPT-4.1 per 1M input and output tokens.",
      },
      {
        label: "Windows vs Linux VM cost",
        prompt:
          "Compare the cost of D4s_v5 with Windows vs Linux in East US. What's the savings with Azure Hybrid Benefit?",
      },
      {
        label: "App Service plan comparison",
        prompt:
          "Compare Azure App Service pricing: Free vs Basic B1 vs Standard S1 vs Premium P1v3 in East US.",
      },
      {
        label: "CDN + Front Door pricing",
        prompt:
          "Compare monthly cost of Azure CDN Standard vs Azure Front Door Standard vs Front Door Premium for 10 TB transfer.",
      },
      {
        label: "Backup and DR pricing",
        prompt:
          "Estimate monthly cost to back up 10 VMs with Azure Backup and protect them with Azure Site Recovery to a paired region.",
      },
      {
        label: "Azure Firewall cost tiers",
        prompt:
          "Compare Azure Firewall Basic vs Standard vs Premium monthly cost including 5 TB data processed.",
      },
    ],
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

function replaceWithChartFallback(option, message) {
  for (const key of Object.keys(option)) {
    delete option[key];
  }

  Object.assign(option, {
    backgroundColor: "transparent",
    title: {
      text: "Chart unavailable",
      subtext: message,
      left: "center",
      top: "middle",
      textStyle: {
        color: "#1f2328",
        fontSize: 16,
        fontWeight: 700,
      },
      subtextStyle: {
        color: "#656d76",
        fontSize: 12,
        width: 320,
        overflow: "break",
      },
    },
  });
}

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

  // Official Azure brand colors for data visualization
  const colors = [
    "#0078D4", // Azure Blue (primary)
    "#50E6FF", // Azure Cyan
    "#008575", // Azure Teal
    "#D83B01", // Azure Orange
    "#8661C5", // Azure Purple
    "#0063B1", // Azure Dark Blue
    "#00B7C3", // Azure Light Teal
    "#E3008C", // Azure Magenta
    "#FFB900", // Azure Yellow
    "#107C10", // Azure Green
    "#B4009E", // Azure Purple (alt)
    "#002050", // Azure Navy
    "#4F6BED", // Azure Indigo
    "#C239B3", // Azure Orchid
    "#767676", // Azure Gray
  ];

  if (chartType === "pie" || chartType === "funnel") {
    const pieData = dataArr.map((d) =>
      Array.isArray(d) ? { name: String(d[0]), value: d[1] } : d,
    );
    return {
      title: {
        text: title,
        left: "center",
        top: 0,
        textStyle: { fontSize: 14, color: "#1f2328" },
      },
      tooltip: { trigger: "item", formatter: "{b}: {c} ({d}%)" },
      legend: {
        bottom: 0,
        left: "center",
        orient: "horizontal",
        type: "scroll",
        textStyle: { color: "#656d76", fontSize: 11 },
      },
      color: colors,
      series: [
        {
          name: seriesName,
          type: chartType,
          radius: chartType === "pie" ? ["35%", "60%"] : undefined,
          center: ["50%", "50%"],
          avoidLabelOverlap: false,
          itemStyle: {
            borderRadius: 10,
            borderColor: "#fff",
            borderWidth: 2,
          },
          label: {
            show: false,
            position: "center",
          },
          emphasis: {
            label: {
              show: true,
              fontSize: 24,
              fontWeight: "bold",
            },
          },
          labelLine: {
            show: false,
          },
          data: pieData,
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

  // Race line chart: multi-series line with end labels and animation
  if (chartType === "race" && isMultiSeries) {
    const seriesKeys = Object.keys(firstItem).filter((k) => k !== "name");
    return {
      animationDuration: 5000,
      title: {
        text: title,
        left: "center",
        textStyle: { fontSize: 14, color: "#1f2328" },
      },
      tooltip: { trigger: "axis", order: "valueDesc" },
      legend: {
        data: seriesKeys,
        bottom: 0,
        type: "scroll",
        textStyle: { color: "#656d76", fontSize: 11 },
      },
      color: colors,
      grid: { left: 60, right: 140, bottom: 40, top: 50 },
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
      series: seriesKeys.map((key) => ({
        name: key,
        type: "line",
        showSymbol: false,
        data: dataArr.map((d) => d[key]),
        endLabel: {
          show: true,
          formatter: (params) => `${params.seriesName}: ${params.value}`,
          fontSize: 11,
        },
        labelLayout: { moveOverlap: "shiftY" },
        emphasis: { focus: "series" },
      })),
    };
  }

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
      // Log chart rendering to App Insights
      const seriesTypes = (option.series || []).map((s) => s.type).join(",");
      const dataPointCount = (option.series || []).reduce(
        (sum, s) => sum + (Array.isArray(s.data) ? s.data.length : 0),
        0,
      );
      window.__trackAppInsightsTrace?.(
        `Chart rendered: isMap=${isMap} seriesTypes=${seriesTypes} dataPoints=${dataPointCount}`,
        {
          isMap: String(isMap),
          seriesTypes,
          dataPoints: String(dataPointCount),
        },
      );
      chartInstances.push(instance);
      const ro = new ResizeObserver(() => instance.resize());
      ro.observe(el);
    });
  };

  if (option._needsMap) {
    ensureWorldMap()
      .then(doMount)
      .catch((error) => {
        window.__trackAppInsightsException?.(error, {
          source: "echarts.world-map",
        });
        replaceWithChartFallback(
          option,
          "World map data could not be loaded. Check browser network and CSP settings.",
        );
        doMount();
      });
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

function requestPresentation() {
  input.value =
    "Generate a FinOps presentation from our conversation findings. Suggest a slide structure with the data we've discussed, and ask me if I want to customize anything before generating.";
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
    let buf = "";

    while (true) {
      const { done, value } = await reader.read();
      if (done) break;

      buf += decoder.decode(value, { stream: true });
      const lines = buf.split("\n");
      buf = lines.pop() || "";

      for (const line of lines) {
        if (!line.startsWith("data: ")) continue;
        const raw = line.slice(6);
        if (raw === "[DONE]") break;

        let data;
        try {
          data = JSON.parse(raw);
        } catch {
          continue;
        }

        switch (data.type) {
          case "delta":
            clearInterval(intentAnimTimer);
            intentAnimTimer = null;
            streamIntent.value = "";
            enqueueText(data.content);
            hasDeltas = true;
            break;

          case "message":
            if (data.content && !hasDeltas) enqueueText(data.content);
            break;

          case "tool_start":
            activeTools.value = [...activeTools.value, data.tool];
            toolCalls.push({
              id: data.id,
              tool: data.tool,
              args: data.args || null,
              result: null,
              error: null,
              success: null,
              durationMs: null,
              done: false,
              expanded: false,
            });
            streamToolCalls.value = [...toolCalls];
            if (data.tool === "report_intent" && data.args) {
              try {
                const parsed =
                  typeof data.args === "string"
                    ? JSON.parse(data.args)
                    : data.args;
                if (parsed.intent) {
                  clearInterval(intentAnimTimer);
                  streamIntent.value = "";
                  let i = 0;
                  const txt = parsed.intent;
                  intentAnimTimer = setInterval(() => {
                    i++;
                    streamIntent.value =
                      i >= txt.length ? txt + "…" : txt.slice(0, i) + "…";
                    if (i >= txt.length) {
                      clearInterval(intentAnimTimer);
                      intentAnimTimer = null;
                    }
                  }, 45);
                }
              } catch {}
            }
            break;

          case "tool_done":
            activeTools.value = activeTools.value.filter(
              (t) => t !== data.tool,
            );
            {
              const tc = toolCalls.find((t) => t.id === data.id);
              if (tc) {
                tc.done = true;
                tc.success = data.success;
                tc.durationMs = data.durationMs;
                tc.result = data.result || null;
                tc.error = data.error || null;
              }
            }
            streamToolCalls.value = [...toolCalls];
            if (
              data.tool === "SuggestFollowUp" &&
              data.success &&
              data.result
            ) {
              try {
                const fu = JSON.parse(data.result);
                if (fu.label && fu.prompt) streamFollowUp.value = fu;
              } catch {}
            }
            break;

          case "chart":
            streamCharts.value = [...streamCharts.value, data.options];
            scrollToBottom();
            break;

          case "pptx_ready":
            pptxReady.value = {
              fileId: data.fileId,
              fileName: data.fileName,
              slideCount: data.slideCount,
            };
            scrollToBottom();
            break;

          case "error":
            streamBuffer.value += `\n**Error:** ${data.message}`;
            break;
        }
      }
    }

    // Flush any remaining animated text before saving
    flushText();

    // Clean up final message: strip thinking lines, fix missing spaces after periods
    const clean = streamBuffer.value
      .replace(/\n*\*[A-Z][^*]{3,60}\.{3}\*\n*/g, "\n")
      .replace(/\.([A-Z])/g, ".\n\n$1")
      .replace(/^\n+/, "")
      .trim();
    const msgObj = {
      role: "assistant",
      content: clean,
      toolCalls: toolCalls.map((tc) => ({ ...tc, expanded: false })),
      charts: [...streamCharts.value],
      followUp: streamFollowUp.value ? { ...streamFollowUp.value } : null,
    };
    if (pptxReady.value) {
      msgObj.pptx = { ...pptxReady.value };
      pptxDownloads.value.push({ ...pptxReady.value });
      pptxReady.value = null;
    }
    messages.value.push(msgObj);
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
        content: `**Connection error:** ${err.message}`,
      });
    }
  } finally {
    clearInterval(intentAnimTimer);
    intentAnimTimer = null;
    flushText();
    streaming.value = false;
    streamBuffer.value = "";
    activeTools.value = [];
    streamToolCalls.value = [];
    streamCharts.value = [];
    streamFollowUp.value = null;
    streamIntent.value = "";
    pptxReady.value = null;
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
  flex-direction: column;
  height: 100%;
  min-height: 0;
  overflow: hidden;
}

/* ── Azure Portal Top Bar ── */
.portal-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  height: 40px;
  background: #0078d4;
  color: #fff;
  padding: 0 12px;
  flex-shrink: 0;
  z-index: 100;
  font-family:
    "Segoe UI",
    "Segoe UI Web (West European)",
    -apple-system,
    BlinkMacSystemFont,
    sans-serif;
}
.portal-header-left {
  display: flex;
  align-items: center;
  gap: 10px;
}
.portal-burger {
  background: none;
  border: none;
  color: #fff;
  cursor: pointer;
  padding: 4px;
  border-radius: 4px;
  display: flex;
  align-items: center;
  justify-content: center;
  transition: background 0.15s;
}
.portal-burger:hover {
  background: rgba(255, 255, 255, 0.15);
}
.portal-title {
  font-size: 14px;
  font-weight: 600;
  letter-spacing: 0;
  color: #fff;
}
.portal-header-right {
  display: flex;
  align-items: center;
  gap: 8px;
}
.portal-user-identity {
  display: flex;
  align-items: center;
  gap: 8px;
  cursor: pointer;
  padding: 2px 8px;
  border-radius: 4px;
  transition: background 0.15s;
}
.portal-user-identity:hover {
  background: rgba(255, 255, 255, 0.12);
}
.portal-user-identity--anon {
  cursor: default;
}
.portal-user-identity--anon:hover {
  background: none;
}
.portal-user-text {
  display: flex;
  flex-direction: column;
  align-items: flex-end;
  line-height: 1.2;
}
.portal-user-email {
  font-size: 12px;
  font-weight: 400;
  color: #fff;
  max-width: 200px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.portal-user-tenant {
  font-size: 10px;
  font-weight: 400;
  color: rgba(255, 255, 255, 0.7);
  text-transform: uppercase;
  letter-spacing: 0.02em;
}
.portal-user-avatar {
  width: 32px;
  height: 32px;
  border-radius: 50%;
  background: rgba(255, 255, 255, 0.2);
  color: #fff;
  display: flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
}

/* ── Main content area below header ── */
.chat-view
  > .auth-overlay
  ~ *:not(.auth-overlay):not(.portal-header):not(.build-badge),
.chat-view-body {
  /* fallback */
}

/* Wrap sidebar + chat + tools in a row below the header */
.sidebar,
.chat-main,
.tools-sidebar {
  /* These are direct children in the flex column; we need them in a row */
}

.sidebar {
  width: 230px;
  flex-shrink: 0;
  border-right: 1px solid #e1dfdd;
  background: #ffffff;
  display: flex;
  flex-direction: column;
  overflow: hidden;
  transition:
    width 0.25s cubic-bezier(0.4, 0, 0.2, 1),
    opacity 0.2s ease;
}
.sidebar--collapsed {
  width: 0;
  opacity: 0;
  border-right: none;
  overflow: hidden;
}

/* ── Portal body: row layout below header ── */
.portal-body {
  display: flex;
  flex-direction: row;
  flex: 1;
  min-height: 0;
  overflow: hidden;
}
.sidebar-scroll {
  flex: 1;
  overflow-y: auto;
  padding: 8px 0;
  display: flex;
  flex-direction: column;
  gap: 2px;
}
.sidebar-category-label {
  font-size: 11px;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.04em;
  color: #605e5c;
  margin-bottom: 4px;
  padding: 0 16px;
}
.sidebar-category--border {
  margin-top: 4px;
  border-top: 1px solid #edebe9;
  padding-top: 8px;
}

.sidebar-category-label--toggle {
  display: flex;
  align-items: center;
  justify-content: space-between;
  cursor: pointer;
  user-select: none;
  border-radius: 2px;
  padding: 4px 16px;
  margin-left: 0;
  margin-right: 0;
}
.sidebar-category-label--toggle:hover {
  background: #f3f2f1;
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
  max-height: 2000px;
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
  padding: 6px 16px;
  border: none;
  border-radius: 0;
  background: transparent;
  font-size: 13px;
  font-weight: 400;
  color: #323130;
  cursor: pointer;
  text-align: left;
  line-height: 1.4;
  transition: background 0.1s;
  font-family: inherit;
}
.sidebar-question:hover {
  background: #f3f2f1;
  color: #0078d4;
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
.sidebar-question-icon--cat-cost {
  background: #e8f0fe;
  color: #0078d4;
}
.sidebar-question-icon--cat-optimize {
  background: #fff0e5;
  color: #d83b01;
}
.sidebar-question-icon--cat-reservations {
  background: #f0ebff;
  color: #8661c5;
}
.sidebar-question-icon--cat-storage {
  background: #e6f5f0;
  color: #008575;
}
.sidebar-question-icon--cat-licensing {
  background: #dafbe1;
  color: #1a7f37;
}
.sidebar-question-icon--cat-governance {
  background: #e8ebf5;
  color: #002050;
}
.sidebar-question-icon--cat-pricing {
  background: #e8f4ff;
  color: #0063b1;
}
.sidebar-question-icon--cat-infra {
  background: #fce8e8;
  color: #cf222e;
}
.sidebar-question-icon--cat-ai {
  background: #fce5f3;
  color: #e3008c;
}
.sidebar-question-icon--cat-advanced {
  background: #eef0ff;
  color: #4f6bed;
}
.sidebar-question-icon--cat-estimator {
  background: #e5f8fa;
  color: #00b7c3;
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
  border-left: 1px solid #e1dfdd;
  background: #ffffff;
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
  background: rgba(0, 120, 212, 0.06);
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
  padding: 2rem 2rem 1rem;
  max-width: 720px;
  margin: 0 auto;
  width: 100%;
  animation: fadeSlideIn 0.35s ease;
}
@keyframes fadeSlideIn {
  from {
    opacity: 0;
    transform: translateY(8px);
  }
  to {
    opacity: 1;
    transform: translateY(0);
  }
}
.es-headline {
  font-size: 1.6rem;
  font-weight: 600;
  margin: 0 0 0.3rem;
  letter-spacing: -0.01em;
  color: #323130;
}
.es-sub {
  font-size: 0.82rem;
  color: var(--text-muted);
  margin: 0 0 1rem;
  line-height: 1.5;
  text-align: center;
  max-width: 480px;
}
.es-connect-bar {
  margin-bottom: 1rem;
}
.es-connect-bar .es-step-btn {
  display: inline-flex;
  align-items: center;
  gap: 8px;
  padding: 0.5rem 1.2rem;
  font-size: 0.82rem;
  border-radius: 10px;
  border: none;
  font-weight: 600;
  cursor: pointer;
}
.es-quick-grid {
  display: grid;
  grid-template-columns: repeat(3, 1fr);
  gap: 8px;
  width: 100%;
}
.es-quick-card {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 10px 12px;
  border: 1px solid var(--border, #d8dee4);
  border-radius: 10px;
  background: var(--surface, #f6f8fa);
  cursor: pointer;
  font: inherit;
  font-size: 0.78rem;
  color: var(--text, #1f2328);
  text-align: left;
  opacity: 0;
  animation: staggerSlideIn 0.5s ease forwards;
  transition:
    border-color 0.15s,
    background-color 0.15s,
    transform 0.15s;
}
.es-quick-card:nth-child(1) {
  animation-delay: 0.5s;
}
.es-quick-card:nth-child(2) {
  animation-delay: 0.6s;
}
.es-quick-card:nth-child(3) {
  animation-delay: 0.7s;
}
.es-quick-card:nth-child(4) {
  animation-delay: 0.5s;
}
.es-quick-card:nth-child(5) {
  animation-delay: 0.6s;
}
.es-quick-card:nth-child(6) {
  animation-delay: 0.7s;
}
.es-quick-card:nth-child(7) {
  animation-delay: 0.5s;
}
.es-quick-card:nth-child(8) {
  animation-delay: 0.6s;
}
.es-quick-card:nth-child(9) {
  animation-delay: 0.7s;
}
.es-quick-card:hover {
  border-color: #0078d4;
  background: rgba(0, 120, 212, 0.04);
  transform: translateY(-1px);
}
.es-quick-icon {
  width: 28px;
  height: 28px;
  border-radius: 8px;
  background: linear-gradient(
    135deg,
    rgba(0, 120, 212, 0.1) 0%,
    rgba(80, 230, 255, 0.12) 100%
  );
  color: #0078d4;
  display: flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
}
.es-quick-icon--teal {
  background: linear-gradient(
    135deg,
    rgba(0, 133, 117, 0.14),
    rgba(0, 183, 195, 0.14)
  );
  color: #008575;
}
.es-quick-icon--orange {
  background: linear-gradient(
    135deg,
    rgba(216, 59, 1, 0.14),
    rgba(255, 185, 0, 0.14)
  );
  color: #d83b01;
}
.es-quick-icon--purple {
  background: linear-gradient(
    135deg,
    rgba(134, 97, 197, 0.14),
    rgba(79, 107, 237, 0.14)
  );
  color: #8661c5;
}
.es-quick-icon--green {
  background: linear-gradient(
    135deg,
    rgba(26, 127, 55, 0.14),
    rgba(45, 164, 78, 0.14)
  );
  color: #1a7f37;
}
.es-quick-icon--pink {
  background: linear-gradient(
    135deg,
    rgba(227, 0, 140, 0.14),
    rgba(255, 109, 182, 0.14)
  );
  color: #e3008c;
}
.es-quick-icon--blue {
  background: linear-gradient(
    135deg,
    rgba(0, 120, 212, 0.14),
    rgba(80, 230, 255, 0.14)
  );
  color: #0078d4;
}
.es-quick-icon--indigo {
  background: linear-gradient(
    135deg,
    rgba(91, 33, 182, 0.14),
    rgba(139, 92, 246, 0.14)
  );
  color: #5b21b6;
}
.es-quick-icon--navy {
  background: linear-gradient(
    135deg,
    rgba(0, 32, 80, 0.14),
    rgba(0, 120, 212, 0.14)
  );
  color: #002050;
}
.es-quick-label {
  font-weight: 600;
  line-height: 1.3;
}
/* Capabilities list (informational, not clickable) */
.es-capabilities {
  width: 100%;
  margin-bottom: 1.25rem;
}
.es-capabilities-grid {
  display: grid;
  grid-template-columns: repeat(2, 1fr);
  gap: 2px;
}
.es-cap-item {
  padding: 12px 16px;
}
.es-cap-title {
  font-size: 0.82rem;
  font-weight: 700;
  color: #1f2328;
  line-height: 1.3;
  margin-bottom: 3px;
}
.es-cap-desc {
  font-size: 0.73rem;
  color: #656d76;
  line-height: 1.45;
}
.es-onboarding {
  width: 100%;
  max-width: 760px;
  margin: 0 0 1.25rem;
  padding: 1rem 1.1rem;
  border: 1px solid rgba(0, 120, 212, 0.14);
  border-radius: 16px;
  background:
    linear-gradient(180deg, rgba(0, 120, 212, 0.05), rgba(255, 255, 255, 0.95)),
    #fff;
  box-shadow: 0 18px 40px rgba(15, 23, 42, 0.06);
}
.es-steps {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 12px;
}
.es-step {
  display: flex;
  gap: 12px;
  align-items: flex-start;
  padding: 0.95rem;
  border: 1px solid rgba(209, 217, 224, 0.9);
  border-radius: 14px;
  background: rgba(255, 255, 255, 0.86);
}
.es-step--active {
  border-color: rgba(0, 120, 212, 0.32);
  box-shadow: inset 0 0 0 1px rgba(0, 120, 212, 0.08);
}
.es-step--done {
  border-color: rgba(45, 164, 78, 0.34);
  background: rgba(45, 164, 78, 0.05);
}
.es-step--blocked {
  opacity: 0.72;
}
.es-step-badge {
  width: 30px;
  height: 30px;
  border-radius: 50%;
  flex-shrink: 0;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  background: #eaf3ff;
  color: #0078d4;
  font-size: 0.84rem;
  font-weight: 800;
}
.es-step--done .es-step-badge {
  background: rgba(45, 164, 78, 0.16);
  color: #1a7f37;
}
.es-step-body {
  min-width: 0;
}
.es-step-title-row {
  display: flex;
  flex-wrap: wrap;
  gap: 6px 10px;
  align-items: center;
  margin-bottom: 0.25rem;
}
.es-step-title {
  font-size: 0.92rem;
  font-weight: 700;
  color: var(--text);
}
.es-step-state {
  padding: 0.12rem 0.45rem;
  border-radius: 999px;
  background: rgba(45, 164, 78, 0.12);
  color: #1a7f37;
  font-size: 0.72rem;
  font-weight: 700;
}
.es-step-state--pending {
  background: rgba(0, 120, 212, 0.1);
  color: #0078d4;
}
.es-step-state--blocked {
  background: rgba(101, 109, 118, 0.12);
  color: var(--text-muted);
}
.es-step-copy {
  margin: 0 0 0.75rem;
  color: var(--text-muted);
  font-size: 0.8rem;
  line-height: 1.5;
}
.es-step-btn {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  min-height: 34px;
  padding: 0.45rem 0.8rem;
  border: 1px solid transparent;
  border-radius: 10px;
  font: inherit;
  font-size: 0.82rem;
  font-weight: 700;
  cursor: pointer;
  transition:
    border-color 0.15s ease,
    background-color 0.15s ease,
    color 0.15s ease,
    transform 0.15s ease;
}
.es-step-btn:hover {
  transform: translateY(-1px);
}
.es-step-btn:disabled {
  opacity: 0.7;
  cursor: wait;
  transform: none;
}
.es-step-btn--github {
  background: #1f2328;
  color: #fff;
}
.es-step-btn--github:hover {
  background: #2f363d;
}
.es-step-btn--azure {
  background: #0078d4;
  color: #fff;
}
.es-step-btn--azure:hover {
  background: #106ebe;
}
/* Feature cards — 2x2 grid */
.es-features {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 10px;
  width: 100%;
  margin-bottom: 1rem;
}
.es-feature {
  display: flex;
  gap: 10px;
  padding: 12px 14px;
  border: 1px solid var(--border, #d8dee4);
  border-radius: 10px;
  background: var(--surface, #f6f8fa);
  transition: border-color 0.2s;
}
.es-feature:hover {
  border-color: #0078d4;
}
.es-feature-icon {
  flex-shrink: 0;
  width: 34px;
  height: 34px;
  border-radius: 8px;
  background: linear-gradient(135deg, #0078d4 0%, #50e6ff 100%);
  color: #fff;
  display: flex;
  align-items: center;
  justify-content: center;
}
.es-feature-icon--safe {
  background: linear-gradient(135deg, #1a7f37 0%, #2da44e 100%);
}
.es-feature-text {
  display: flex;
  flex-direction: column;
  gap: 2px;
  min-width: 0;
}
.es-feature-text strong {
  font-size: 0.82rem;
  font-weight: 700;
  color: var(--text, #1f2328);
}
.es-feature-text span {
  font-size: 0.75rem;
  color: var(--text-muted);
  line-height: 1.4;
}
/* API pills */
.es-api-pills {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
  justify-content: center;
  margin-bottom: 1rem;
}
.es-api-pill {
  padding: 3px 10px;
  border-radius: 10px;
  border: 1px solid var(--border, #d8dee4);
  font-size: 0.72rem;
  color: var(--text-muted);
  background: var(--surface, #f6f8fa);
}
/* Comparison table */
.es-compare {
  width: 100%;
  margin-bottom: 1rem;
  border: 1px solid var(--border, #d8dee4);
  border-radius: 10px;
  overflow: hidden;
}
.es-compare-table {
  width: 100%;
  border-collapse: collapse;
  font-size: 0.75rem;
  line-height: 1.4;
}
.es-compare-table th {
  background: var(--surface, #f6f8fa);
  font-weight: 700;
  text-align: left;
  padding: 8px 10px;
  color: var(--text, #1f2328);
  border-bottom: 1px solid var(--border, #d8dee4);
  font-size: 0.72rem;
  text-transform: uppercase;
  letter-spacing: 0.03em;
}
.es-compare-table th.es-compare-us {
  color: #0078d4;
}
.es-compare-table td {
  padding: 6px 10px;
  border-bottom: 1px solid rgba(0, 0, 0, 0.04);
  color: var(--text-muted);
}
.es-compare-table td:first-child {
  font-weight: 600;
  color: var(--text, #1f2328);
  min-width: 120px;
}
.es-compare-table td.es-compare-us {
  color: var(--text, #1f2328);
}
.es-compare-table tr:last-child td {
  border-bottom: none;
}
.es-compare-table tr:hover td {
  background: rgba(0, 120, 212, 0.03);
}
/* Prompts */
.es-prompts {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
  justify-content: center;
  margin-bottom: 1rem;
}
.es-prompt {
  padding: 6px 14px;
  border: 1px solid var(--border, #d8dee4);
  border-radius: 18px;
  background: transparent;
  color: var(--text, #1f2328);
  font-size: 0.8rem;
  cursor: pointer;
  transition:
    border-color 0.15s,
    color 0.15s;
}
.es-prompt:hover {
  border-color: #0969da;
  color: #0969da;
}
@media (max-width: 600px) {
  .es-features {
    grid-template-columns: 1fr;
  }
  .es-headline {
    font-size: 1.6rem;
  }
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
  background: #e1dfdd;
  color: #323130;
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
  background: #323130;
  color: #fff;
  font-size: 11px;
  font-weight: 700;
  display: flex;
  align-items: center;
  justify-content: center;
}
.ai-header {
  display: flex;
  flex-direction: row;
  align-items: center;
  gap: 0.5rem;
  min-height: 28px;
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
.stream-intent {
  font-style: italic;
  color: #6b7280;
  font-size: 0.88rem;
  font-weight: 500;
  white-space: normal;
  line-height: 1.4;
  animation: intent-in 0.3s ease-out;
}
@keyframes intent-in {
  from {
    opacity: 0;
    transform: translateX(-8px);
  }
  to {
    opacity: 1;
    transform: translateX(0);
  }
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
  max-width: 900px;
  margin: 0 auto;
  width: 100%;
  padding-bottom: max(0.75rem, env(safe-area-inset-bottom));
  display: flex;
  align-items: center;
  gap: 6px;
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
  flex: 1;
  min-width: 0;
}
.input-side-actions {
  display: flex;
  align-items: center;
  gap: 4px;
  flex-shrink: 0;
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
  color: #323130;
  font-size: 0.875rem;
  font-family: inherit;
  padding: 0;
  outline: none;
  line-height: 1.5;
}
.input-field::placeholder {
  color: #a19f9d;
}
.input-pill-btn {
  flex-shrink: 0;
  height: 44px;
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 6px;
  padding: 0 14px;
  border-radius: 9999px;
  border: 1px solid #e4e4e7;
  background: #fff;
  color: #a1a1aa;
  cursor: pointer;
  transition: all 0.15s;
  font-family: inherit;
  font-size: 0.875rem;
  font-weight: 500;
}
.input-pill-btn:not(:disabled) {
  color: #3f3f46;
  border-color: #d4d4d8;
}
.input-pill-btn:hover:not(:disabled) {
  background: #f4f4f5;
  color: #18181b;
  border-color: #a1a1aa;
}
.input-pill-btn--disabled {
  opacity: 0.4;
  cursor: default;
}
.input-pill-label {
  line-height: 1;
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
  background: #0078d4;
  color: #fff;
}
.action-btn--active:hover {
  background: #106ebe;
}
.action-btn--disabled {
  background: #e4e4e7;
  color: #a1a1aa;
  cursor: default;
}
.action-btn--stop {
  background: #323130;
  color: #fff;
}
.action-btn--stop:hover {
  background: #484644;
}
.sidebar-footer {
  flex-shrink: 0;
  padding: 8px 12px;
  border-top: 1px solid #edebe9;
  display: flex;
  flex-direction: column;
  gap: 8px;
  background: #fff;
}
.sidebar-linkedin {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 7px;
  padding: 7px 10px;
  border-radius: 8px;
  border: 1px solid #0a66c2;
  color: #0a66c2;
  font-size: 12px;
  font-weight: 600;
  text-decoration: none;
  transition: background 0.15s;
}
.sidebar-linkedin:hover {
  background: rgba(10, 102, 194, 0.08);
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
  background: #323130;
  color: #fff;
}
.login-btn--github:hover:not(:disabled) {
  background: #0078d4;
}
.login-btn:disabled,
.azure-connect-btn:disabled {
  opacity: 0.7;
  cursor: wait;
}
.auth-spinner {
  width: 16px;
  height: 16px;
  border: 2px solid rgba(255, 255, 255, 0.3);
  border-top-color: #fff;
  border-radius: 50%;
  animation: auth-spin 0.6s linear infinite;
  flex-shrink: 0;
}
.auth-spinner--azure {
  border-color: rgba(0, 120, 212, 0.2);
  border-top-color: #0078d4;
}
@keyframes auth-spin {
  to {
    transform: rotate(360deg);
  }
}

/* Full-screen auth overlay */
.auth-overlay {
  position: fixed;
  inset: 0;
  z-index: 9999;
  display: flex;
  align-items: center;
  justify-content: center;
  background: rgba(255, 255, 255, 0.85);
  backdrop-filter: blur(4px);
  animation: auth-overlay-in 0.2s ease;
}
@keyframes auth-overlay-in {
  from {
    opacity: 0;
  }
  to {
    opacity: 1;
  }
}
.auth-overlay-card {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 16px;
  padding: 2.5rem 3rem;
  border-radius: 16px;
  background: #fff;
  box-shadow: 0 8px 32px rgba(0, 0, 0, 0.1);
}
.auth-overlay-spinner {
  width: 40px;
  height: 40px;
  border: 3px solid rgba(0, 120, 212, 0.15);
  border-top-color: #0078d4;
  border-radius: 50%;
  animation: auth-spin 0.7s linear infinite;
}
.auth-overlay-text {
  font-size: 16px;
  font-weight: 600;
  color: #1f2328;
  margin: 0;
}
.auth-overlay-sub {
  font-size: 13px;
  color: #656d76;
  margin: 0;
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
  font-size: 13px;
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
/* Incremental consent addon buttons */
.azure-addons {
  display: flex;
  gap: 6px;
  margin-top: 6px;
  flex-wrap: wrap;
}
.azure-addon-btn {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  padding: 5px 10px;
  border-radius: 6px;
  border: 1px dashed var(--border, #d1d9e0);
  background: transparent;
  color: var(--text-muted, #656d76);
  font-size: 13px;
  font-weight: 500;
  cursor: pointer;
  transition: all 0.15s;
}
.azure-addon-btn:hover {
  border-color: #0078d4;
  border-style: solid;
  color: #0078d4;
  background: rgba(0, 120, 212, 0.04);
}
.azure-addon-icon {
  font-weight: 700;
  font-size: 12px;
}
.azure-addon-active {
  display: inline-flex;
  align-items: center;
  padding: 5px 10px;
  border-radius: 6px;
  background: rgba(0, 120, 212, 0.08);
  color: #0078d4;
  font-size: 13px;
  font-weight: 500;
}
.azure-revoke-btn {
  width: 100%;
  padding: 6px 8px;
  margin-top: 6px;
  border-radius: 5px;
  border: none;
  background: transparent;
  color: var(--text-muted, #8b949e);
  font-size: 13px;
  cursor: pointer;
  transition: color 0.15s;
  text-decoration: underline;
  text-underline-offset: 2px;
}
.azure-revoke-btn:hover {
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
  bottom: 12px;
  right: 16px;
  font-size: 11px;
  font-family: monospace;
  color: #1f2328;
  background: rgba(200, 200, 200, 0.35);
  padding: 2px 8px;
  border-radius: 6px;
  opacity: 0.85;
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
  .build-badge {
    display: none;
  }
  .portal-user-text {
    display: none;
  }
  .es-sub {
    font-size: 0.76rem;
  }
  .es-tagline-mobile {
    display: none;
  }
  .es-headline {
    font-size: 1.3rem;
  }
  .es-quick-grid {
    grid-template-columns: 1fr 1fr;
  }
  .es-diff-grid {
    grid-template-columns: 1fr;
  }
  .empty-state {
    padding: 1rem 1rem 0.5rem;
    justify-content: center;
    flex: 1;
  }
  .messages-inner {
    padding: 0.75rem 0.75rem;
    flex: 1;
    display: flex;
    flex-direction: column;
  }
  .input-area {
    padding: 0.5rem 0.75rem;
  }
  .input-pill-label {
    display: none;
  }
  .input-pill-btn {
    padding: 0;
    width: 32px;
    border: none;
    background: transparent;
  }
}

/* ── Presentation Generate / Download ── */
.pptx-suggest {
  display: flex;
  justify-content: center;
  padding: 0 1rem 0.25rem;
  max-width: 800px;
  margin: 0 auto;
  width: 100%;
}
.pptx-suggest-btn {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  padding: 7px 16px;
  border-radius: 9999px;
  border: 1px solid #d4d4d8;
  background: #fff;
  color: #3f3f46;
  font-size: 0.8rem;
  font-family: inherit;
  cursor: pointer;
  transition: all 0.15s;
}
.pptx-suggest-btn:hover {
  border-color: #0078d4;
  color: #0078d4;
  background: rgba(0, 120, 212, 0.04);
}
.pptx-download-bar {
  display: flex;
  justify-content: center;
  padding: 0 1rem 0.5rem;
  max-width: 800px;
  margin: 0 auto;
  width: 100%;
}
.pptx-download-btn {
  display: inline-flex;
  align-items: center;
  gap: 8px;
  padding: 8px 18px;
  border-radius: 8px;
  background: #0078d4;
  color: #fff;
  font-size: 0.85rem;
  font-weight: 500;
  text-decoration: none;
  cursor: pointer;
  transition: all 0.15s;
  border: none;
}
.pptx-download-btn:hover {
  background: #106ebe;
}
.pptx-inline-download {
  margin-top: 0.5rem;
}
/* ── Follow-up question buttons ── */
.follow-up-buttons {
  display: flex;
  flex-wrap: wrap;
  gap: 0.5rem;
  margin-top: 0.75rem;
}
.follow-up-btn {
  background: rgba(0, 120, 212, 0.08);
  color: #0078d4;
  border: 1px solid rgba(0, 120, 212, 0.25);
  border-radius: 20px;
  padding: 0.35rem 0.85rem;
  font-size: 0.82rem;
  cursor: pointer;
  transition:
    background 0.15s,
    border-color 0.15s;
  line-height: 1.4;
  text-align: left;
}
.follow-up-btn:hover {
  background: rgba(0, 120, 212, 0.15);
  border-color: #0078d4;
}
/* ── Mobile auth bar ── */
.mobile-auth-bar {
  display: none;
  align-items: center;
  gap: 8px;
  padding: 0 0.75rem 0.25rem;
  flex-wrap: wrap;
  justify-content: center;
}
.mobile-auth-btn {
  display: inline-flex;
  align-items: center;
  gap: 8px;
  padding: 10px 22px;
  border-radius: 9999px;
  font-size: 0.9rem;
  font-weight: 600;
  font-family: inherit;
  cursor: pointer;
  transition: all 0.15s;
  border: none;
  min-height: 40px;
}
.mobile-auth-btn--github {
  background: #323130;
  color: #fff;
}
.mobile-auth-btn--github:hover:not(:disabled) {
  background: #0078d4;
}
.mobile-auth-btn--azure {
  background: var(--surface, #f6f8fa);
  color: var(--text, #1f2328);
  border: 1px solid var(--border, #d8dee4);
}
.mobile-auth-btn--azure:hover:not(:disabled) {
  border-color: #0078d4;
  color: #0078d4;
}
.mobile-auth-btn:disabled {
  opacity: 0.7;
  cursor: wait;
}
.mobile-auth-item {
  display: flex;
  align-items: center;
}
.mobile-azure-status {
  gap: 5px;
  padding: 4px 10px;
  border-radius: 9999px;
  background: rgba(0, 120, 212, 0.06);
  border: 1px solid rgba(0, 120, 212, 0.15);
  font-size: 0.75rem;
  color: var(--text);
}
.mobile-azure-email {
  max-width: 100px;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
.mobile-user-info {
  gap: 6px;
  padding: 4px 8px;
  border-radius: 9999px;
  background: var(--surface, #f6f8fa);
  border: 1px solid var(--border, #d8dee4);
}
.mobile-user-avatar {
  width: 20px;
  height: 20px;
  border-radius: 50%;
}
.mobile-user-name {
  font-size: 0.75rem;
  font-weight: 500;
  color: var(--text);
  max-width: 80px;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
.es-tagline-mobile {
  display: none;
  font-size: 0.85rem;
  color: var(--text-muted);
  margin: 0.5rem 1rem 0;
  padding: 0 1rem;
  text-align: center;
  line-height: 1.5;
}
/* Mobile overrides — must come after base rules */
@media (max-width: 768px) {
  .mobile-auth-bar {
    display: flex !important;
  }
  .es-tagline-mobile {
    display: block !important;
  }
}
</style>
