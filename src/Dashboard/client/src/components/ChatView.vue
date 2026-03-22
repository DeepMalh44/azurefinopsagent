<template>
  <div class="chat-view">
    <!-- Auth loading overlay -->
    <div v-if="authLoading" class="auth-overlay">
      <div class="auth-overlay-card">
        <div class="auth-overlay-spinner"></div>
        <p class="auth-overlay-text">
          {{
            authLoading === "github"
              ? "Connecting to GitHub..."
              : "Connecting to Azure..."
          }}
        </p>
        <p class="auth-overlay-sub">You will be redirected to sign in</p>
      </div>
    </div>

    <!-- Left sidebar -->
    <aside class="sidebar">
      <div class="sidebar-scroll">
        <!-- FinOps Prompt Categories -->
        <div
          v-for="cat in finopsCategories"
          :key="cat.key"
          class="sidebar-category"
          :class="{ 'sidebar-category--border': cat !== finopsCategories[0] }"
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
              :class="{
                'sidebar-question--locked': cat.requiresAzure
                  ? !azureConnected
                  : !user,
              }"
              :disabled="
                streaming || (cat.requiresAzure ? !azureConnected : !user)
              "
              :title="
                cat.requiresAzure
                  ? azureConnected
                    ? q.prompt
                    : 'Connect Azure to unlock'
                  : user
                    ? q.prompt
                    : 'Sign in with GitHub to unlock'
              "
              @click="sendQuestion(q.prompt)"
            >
              <span class="sidebar-question-icon sidebar-question-icon--finops">
                <svg
                  v-if="cat.requiresAzure ? !azureConnected : !user"
                  width="10"
                  height="10"
                  viewBox="0 0 16 16"
                  fill="currentColor"
                >
                  <path
                    d="M4 7V5a4 4 0 118 0v2h1a1 1 0 011 1v6a1 1 0 01-1 1H3a1 1 0 01-1-1V8a1 1 0 011-1h1zm2 0h4V5a2 2 0 10-4 0v2z"
                  />
                </svg>
                <template v-else>{{ cat.icon }}</template>
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
        <!-- LinkedIn contact -->
        <a
          href="https://www.linkedin.com/in/alirezafarahnak/"
          target="_blank"
          rel="noopener noreferrer"
          class="sidebar-linkedin"
        >
          <svg width="14" height="14" viewBox="0 0 24 24" fill="currentColor">
            <path
              d="M20.447 20.452h-3.554v-5.569c0-1.328-.027-3.037-1.852-3.037-1.853 0-2.136 1.445-2.136 2.939v5.667H9.351V9h3.414v1.561h.046c.477-.9 1.637-1.85 3.37-1.85 3.601 0 4.267 2.37 4.267 5.455v6.286zM5.337 7.433c-1.144 0-2.063-.926-2.063-2.065 0-1.138.92-2.063 2.063-2.063 1.14 0 2.064.925 2.064 2.063 0 1.139-.925 2.065-2.064 2.065zm1.782 13.019H3.555V9h3.564v11.452zM22.225 0H1.771C.792 0 0 .774 0 1.729v20.542C0 23.227.792 24 1.771 24h20.451C23.2 24 24 23.227 24 22.271V1.729C24 .774 23.2 0 22.222 0h.003z"
            />
          </svg>
          Contact on LinkedIn
        </a>

        <!-- Not logged in: show GitHub login -->
        <template v-if="!user">
          <button
            class="login-btn login-btn--github"
            :disabled="authLoading === 'github'"
            @click="startAuth('github', '/auth/github')"
          >
            <span v-if="authLoading === 'github'" class="auth-spinner"></span>
            <svg
              v-else
              width="16"
              height="16"
              viewBox="0 0 16 16"
              fill="currentColor"
            >
              <path
                d="M8 0c4.42 0 8 3.58 8 8a8.013 8.013 0 0 1-5.45 7.59c-.4.08-.55-.17-.55-.38 0-.27.01-1.13.01-2.2 0-.75-.25-1.23-.54-1.48 1.78-.2 3.65-.88 3.65-3.95 0-.88-.31-1.59-.82-2.15.08-.2.36-1.02-.08-2.12 0 0-.67-.22-2.2.82-.64-.18-1.32-.27-2-.27-.68 0-1.36.09-2 .27-1.53-1.03-2.2-.82-2.2-.82-.44 1.1-.16 1.92-.08 2.12-.51.56-.82 1.28-.82 2.15 0 3.06 1.86 3.75 3.64 3.95-.23.2-.44.55-.51 1.07-.46.21-1.61.55-2.33-.66-.15-.24-.6-.83-1.23-.82-.67.01-.27.38.01.53.34.19.73.9.82 1.13.16.45.68 1.31 2.69.94 0 .67.01 1.3.01 1.49 0 .21-.15.45-.55.38A7.995 7.995 0 0 1 0 8c0-4.42 3.58-8 8-8Z"
              />
            </svg>
            {{
              authLoading === "github" ? "Connecting..." : "Sign in with GitHub"
            }}
          </button>
        </template>

        <!-- Logged in -->
        <template v-else>
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
            <h1 class="es-headline">Azure FinOps Agent</h1>
            <p class="es-tagline">
              AI-powered cloud cost optimization for Azure
            </p>
            <p v-if="!user" class="es-sub">
              Sign in with GitHub to get started. Connect your Azure tenant to
              unlock real-time cost analysis, optimization recommendations,
              interactive charts, and executive-ready PowerPoint reports — all
              through natural conversation.
            </p>
            <p v-else class="es-sub">
              Ask about costs, pricing, reservations, licensing, or
              infrastructure. I query your Azure tenant APIs in real time and
              visualize the results.
            </p>

            <!-- Feature cards -->
            <div class="es-features">
              <div class="es-feature">
                <div class="es-feature-icon">
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
                    <polygon points="13 2 3 14 12 14 11 22 21 10 12 10 13 2" />
                  </svg>
                </div>
                <div class="es-feature-text">
                  <strong>Real Azure APIs</strong>
                  <span
                    >Calls Cost Management, Advisor, Resource Graph, Billing,
                    Monitor, Microsoft Graph &amp; Log Analytics with your
                    delegated token — not canned responses</span
                  >
                </div>
              </div>
              <div class="es-feature">
                <div class="es-feature-icon">
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
                    <rect x="3" y="3" width="7" height="7" />
                    <rect x="14" y="3" width="7" height="7" />
                    <rect x="14" y="14" width="7" height="7" />
                    <rect x="3" y="14" width="7" height="7" />
                  </svg>
                </div>
                <div class="es-feature-text">
                  <strong>Multi-step analysis</strong>
                  <span
                    >Chains queries across APIs, processes data with Python,
                    builds interactive ECharts visualizations, and identifies
                    cost anomalies automatically</span
                  >
                </div>
              </div>
              <div class="es-feature">
                <div class="es-feature-icon">
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
                    <rect x="2" y="3" width="20" height="14" rx="2" />
                    <line x1="8" y1="21" x2="16" y2="21" />
                    <line x1="12" y1="17" x2="12" y2="21" />
                  </svg>
                </div>
                <div class="es-feature-text">
                  <strong>PowerPoint generation</strong>
                  <span
                    >Generates a first-draft FinOps presentation with charts and
                    data tables — ready to discuss with stakeholders and
                    leadership</span
                  >
                </div>
              </div>
              <div class="es-feature">
                <div class="es-feature-icon es-feature-icon--safe">
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
                    <path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z" />
                  </svg>
                </div>
                <div class="es-feature-text">
                  <strong>100% read-only</strong>
                  <span
                    >Uses only read-only APIs — will never create, modify, or
                    delete any resource in your Azure tenant. Completely safe to
                    run on production</span
                  >
                </div>
              </div>
            </div>

            <!-- Comparison table -->
            <div class="es-compare">
              <table class="es-compare-table">
                <thead>
                  <tr>
                    <th>Capability</th>
                    <th>Azure Copilot</th>
                    <th class="es-compare-us">FinOps Agent</th>
                  </tr>
                </thead>
                <tbody>
                  <tr>
                    <td>Scope</td>
                    <td>Single subscription in portal context</td>
                    <td class="es-compare-us">
                      All subscriptions + management groups — Resource Graph KQL
                      across entire tenant in one query
                    </td>
                  </tr>
                  <tr>
                    <td>Cost analysis &amp; breakdown</td>
                    <td>Basic portal summaries</td>
                    <td class="es-compare-us">
                      Deep multi-dimensional — by service, RG, tag,
                      subscription, region, with interactive charts
                    </td>
                  </tr>
                  <tr>
                    <td>VM &amp; resource right-sizing</td>
                    <td>Shows Advisor tips</td>
                    <td class="es-compare-us">
                      Queries CPU/memory metrics via Monitor + Advisor,
                      cross-references with pricing to quantify savings per VM
                    </td>
                  </tr>
                  <tr>
                    <td>Reservation &amp; savings plan analysis</td>
                    <td>Limited visibility</td>
                    <td class="es-compare-us">
                      Utilization audit, expiring RI alerts, exchange analysis,
                      coverage gap detection, SP vs RI comparison
                    </td>
                  </tr>
                  <tr>
                    <td>Multi-step chained analysis</td>
                    <td>Single-turn Q&amp;A</td>
                    <td class="es-compare-us">
                      Chains 5+ API calls, processes with Python, correlates
                      across data sources automatically
                    </td>
                  </tr>
                  <tr>
                    <td>Interactive visualizations</td>
                    <td>Static portal charts</td>
                    <td class="es-compare-us">
                      ECharts: bar, line, pie, scatter, world maps, treemaps,
                      radar, gauge — inline in chat
                    </td>
                  </tr>
                  <tr>
                    <td>Chargeback &amp; tagging</td>
                    <td>Manual Cost Management views</td>
                    <td class="es-compare-us">
                      Auto-generates chargeback reports by tag/owner, audits
                      tagging gaps, quantifies cost of untagged resources
                    </td>
                  </tr>
                  <tr>
                    <td>M365 license optimization</td>
                    <td>Not available</td>
                    <td class="es-compare-us">
                      Queries Microsoft Graph for unused licenses, Copilot seat
                      ROI, license waste quantification
                    </td>
                  </tr>
                  <tr>
                    <td>Infrastructure deep-dive</td>
                    <td>Not available</td>
                    <td class="es-compare-us">
                      KQL on Log Analytics — ingestion costs, VM perf metrics,
                      container insights, activity audit trail
                    </td>
                  </tr>
                  <tr>
                    <td>Security &amp; cost correlation</td>
                    <td>Not available</td>
                    <td class="es-compare-us">
                      Defender for Cloud scores + cost data — find expensive
                      resources with security risks
                    </td>
                  </tr>
                  <tr>
                    <td>Carbon emissions</td>
                    <td>Not available</td>
                    <td class="es-compare-us">
                      Azure Carbon API — emissions by service/region, impact
                      analysis for region migration
                    </td>
                  </tr>
                  <tr>
                    <td>PowerPoint export</td>
                    <td>Not available</td>
                    <td class="es-compare-us">
                      Generates FinOps .pptx with charts &amp; data tables —
                      ready for stakeholder review
                    </td>
                  </tr>
                  <tr>
                    <td>Code execution</td>
                    <td>Not available</td>
                    <td class="es-compare-us">
                      Python, bash, SQLite for complex data processing,
                      calculations, and custom reporting
                    </td>
                  </tr>
                </tbody>
              </table>
            </div>

            <!-- Quick start -->
            <div v-if="user" class="es-prompts">
              <button
                v-if="azureConnected"
                class="es-prompt"
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
                class="es-prompt"
                @click="
                  sendPrompt(
                    'What cost optimization recommendations does Azure Advisor have for me? Show estimated savings.',
                  )
                "
              >
                Advisor savings
              </button>
              <button
                v-if="azureConnected"
                class="es-prompt"
                @click="
                  sendPrompt(
                    'Create an executive summary of my Azure spend with charts.',
                  )
                "
              >
                Executive summary
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
                    Download {{ msg.pptx.fileName }} ({{ msg.pptx.slideCount }}
                    slides)
                  </a>
                </div>
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

      <!-- Generate Presentation button -->
      <div
        v-if="user && messages.length >= 2 && !streaming"
        class="pptx-suggest"
      >
        <button class="pptx-suggest-btn" @click="requestPresentation">
          <svg
            xmlns="http://www.w3.org/2000/svg"
            width="15"
            height="15"
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            stroke-width="2"
            stroke-linecap="round"
            stroke-linejoin="round"
          >
            <rect x="2" y="3" width="20" height="14" rx="2" />
            <line x1="8" y1="21" x2="16" y2="21" />
            <line x1="12" y1="17" x2="12" y2="21" />
          </svg>
          Generate a presentation of the findings?
        </button>
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
          Download {{ pptxReady.fileName }} ({{ pptxReady.slideCount }} slides)
        </a>
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
const pptxReady = ref(null);
const pptxDownloads = ref([]);
const messagesEl = ref(null);
const inputEl = ref(null);
const chartInstances = [];
let intentAnimTimer = null;
const hoveredTool = ref(null);
const collapsedSections = reactive({
  subs: true,
  finops_cost: true,
  finops_optimize: true,
  finops_reservations: true,
  finops_storage: true,
  finops_licensing: true,
  finops_governance: true,
  finops_pricing: true,
  finops_infra: true,
});
function toggleSection(key) {
  collapsedSections[key] = !collapsedSections[key];
}
const buildSha = ref("");
const buildNumber = ref("0");
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

function startAuth(provider, url) {
  authLoading.value = provider;
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
  pptxReady.value = null;
  pptxDownloads.value = [];
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
const finopsCategories = [
  {
    key: "finops_cost",
    label: "Cost Analysis",
    icon: "$",
    requiresAzure: true,
    prompts: [
      {
        label: "Cost this month",
        prompt:
          "Show my Azure cost for the current month grouped by service. Chart it.",
      },
      {
        label: "Cost trend",
        prompt:
          "Show my daily Azure spend for the last 30 days as a line chart.",
      },
      {
        label: "Cost by resource group",
        prompt:
          "Break down my current month's Azure cost by resource group and show a bar chart.",
      },
      {
        label: "Month-over-month",
        prompt:
          "Compare this month's Azure spend to last month by service. Highlight the biggest increases and show a chart.",
      },
      {
        label: "Top 10 resources",
        prompt:
          "What are my top 10 most expensive Azure resources this month? Show a chart.",
      },
      {
        label: "Cost anomaly detection",
        prompt:
          "Identify any cost anomalies or unexpected spikes in my Azure spend over the past 14 days. Show me the daily cost trend and flag the days with abnormal spend.",
      },
      {
        label: "Budget vs actual",
        prompt:
          "Show my Azure budgets vs actual spend for the current billing period. Which budgets are at risk of being exceeded? Show a gauge chart per budget.",
      },
      {
        label: "Cost forecast",
        prompt:
          "Based on my current spending trend, forecast my Azure bill for the rest of this month. Show the projected cost vs budget as a line chart.",
      },
      {
        label: "Cost by tag",
        prompt:
          "Break down my Azure costs by the cost-center tag for the current month. Show a pie chart and table. Which cost centers have the highest spend?",
      },
      {
        label: "Cost by subscription",
        prompt:
          "Compare Azure costs across all my subscriptions for the current month. Show a bar chart ranking subscriptions by spend, and a table with subscription name, cost, and month-over-month change.",
      },
      {
        label: "Cost by location",
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
  {
    key: "finops_optimize",
    label: "Optimization",
    icon: "O",
    requiresAzure: true,
    prompts: [
      {
        label: "Advisor recommendations",
        prompt:
          "What cost optimization recommendations does Azure Advisor have for me? Group them by impact (high, medium, low) and show the estimated annual savings for each.",
      },
      {
        label: "Unattached disks savings",
        prompt:
          "How many unattached disks do I have and what would be my savings if I remove them? List them in a table ranked by highest savings potential at the top. Include the disk name, size, SKU, monthly cost, resource group, and all tags so I can plan outreach to the responsible teams.",
      },
      {
        label: "Optimize resource group",
        prompt:
          "I need to cost optimize my most expensive resource group. First show me my top 5 resource groups by cost this month, then give me the top Advisor recommendations and idle resources for the most expensive one.",
      },
      {
        label: "Idle resources cleanup",
        prompt:
          "Find all idle or underutilized VMs, disks, public IPs, App Service plans, and load balancers across my subscriptions. For each, show the resource name, type, resource group, monthly cost, and tags in a table sorted by cost. What's my total potential savings?",
      },
      {
        label: "Dev/test savings",
        prompt:
          "Identify resources in dev/test environments (by tag or naming convention) that could be shut down, scaled down, or deallocated to save costs. Show a table with the resource, current SKU, recommended action, and estimated monthly savings.",
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
        label: "AKS cost optimization",
        prompt:
          "Analyze my AKS clusters. Show each cluster's node pool configuration, VM sizes, node count, and monthly cost. Identify over-provisioned node pools where CPU/memory requests are significantly below capacity. Recommend right-sizing actions with estimated savings.",
      },
      {
        label: "App Service consolidation",
        prompt:
          "List all my App Service plans with their pricing tier, instance count, and the number of apps hosted on each. Identify plans with low utilization or only one app that could be consolidated. Show potential savings from merging underutilized plans.",
      },
      {
        label: "Network cost review",
        prompt:
          "Review my network-related costs: ExpressRoute circuits, VPN gateways, NAT gateways, public IPs, and Application Gateways. List each resource with its SKU, monthly cost, and utilization. Flag any that appear oversized or idle.",
      },
    ],
  },
  {
    key: "finops_reservations",
    label: "Reservations & Commitments",
    icon: "R",
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
        label: "RI exchange analysis",
        prompt:
          "Analyze my current reservations for exchange opportunities. Which reservations are underutilized and could be exchanged for a better-fitting SKU or region? Show the current reservation, utilization %, and recommended exchange target.",
      },
      {
        label: "Savings plan recommendations",
        prompt:
          "Based on my compute spend patterns, should I buy a savings plan or a reservation? Compare the savings from each option for my top 5 compute workloads. Show a table with resource, current monthly cost, RI savings, SP savings, and recommendation.",
      },
    ],
  },
  {
    key: "finops_storage",
    label: "Storage & Data",
    icon: "S",
    requiresAzure: true,
    prompts: [
      {
        label: "Storage optimization",
        prompt:
          "Find storage accounts with no recent access and recommend tiering or cleanup for cost savings. Show the storage account, current tier, last access date, size, monthly cost, and recommended tier in a table.",
      },
      {
        label: "GPv1 to GPv2 migration",
        prompt:
          "I need to upgrade storage from GPv1 to GPv2. List all my storage accounts, identify which are still GPv1, and do a cost analysis comparing current GPv1 costs vs projected GPv2 costs. Show the cost difference per account in a table so I can plan the migration.",
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
        label: "Storage tier pricing",
        prompt:
          "Compare the cost of storing 10 TB in Azure Blob Storage across Hot, Cool, Cold, and Archive tiers in East US. Include per-GB storage cost and per-10K read/write transaction costs in a table.",
      },
      {
        label: "Log Analytics ingestion cost",
        prompt:
          "Analyze my Log Analytics workspace ingestion costs. Show data volume by table (e.g. AzureDiagnostics, Perf, ContainerLog) over the past 30 days. Which tables are the biggest cost drivers? Recommend tables to move to Basic Logs or archive tier.",
      },
      {
        label: "Database cost analysis",
        prompt:
          "List all my Azure SQL databases and Cosmos DB accounts with their pricing tier, DTU/vCore/RU configuration, and monthly cost. Identify databases that are over-provisioned or could use serverless/auto-pause to save costs.",
      },
    ],
  },
  {
    key: "finops_licensing",
    label: "Licensing & Hybrid",
    icon: "L",
    requiresAzure: true,
    prompts: [
      {
        label: "License optimization",
        prompt:
          "Analyze my Microsoft 365 and Azure license usage. List all license types with total purchased, assigned, and unused counts. Show unused licenses with their monthly cost — what's my total waste from unassigned licenses?",
      },
      {
        label: "Hybrid benefit check",
        prompt:
          "Which of my Windows VMs and SQL databases are NOT using Azure Hybrid Benefit? List them with their current monthly cost and the savings I'd get by enabling AHUB. Show total potential savings.",
      },
      {
        label: "M365 Copilot ROI",
        prompt:
          "Show my Microsoft 365 Copilot license usage. How many seats are assigned vs actively used? Which users haven't used Copilot in the last 30 days? Calculate the monthly waste from inactive Copilot licenses.",
      },
      {
        label: "Dev/Test subscription savings",
        prompt:
          "Are my dev/test workloads running under a Dev/Test subscription with discounted rates? Identify subscriptions tagged as dev/test that aren't using Enterprise Dev/Test pricing, and estimate the savings from switching.",
      },
      {
        label: "Windows Server licensing",
        prompt:
          "Audit my Windows Server VMs. How many are using Azure Hybrid Benefit vs pay-as-you-go licensing? Show the per-VM cost difference and total savings opportunity from enabling AHUB on all eligible VMs.",
      },
    ],
  },
  {
    key: "finops_governance",
    label: "Governance & Reporting",
    icon: "G",
    requiresAzure: true,
    prompts: [
      {
        label: "Chargeback report",
        prompt:
          "Generate a chargeback report for the current month. Break down costs by the owner tag or cost-center tag. Show a bar chart and table with each team's total spend, top services, and month-over-month change.",
      },
      {
        label: "Executive cost summary",
        prompt:
          "Create an executive summary of my Azure spend: total cost this month, month-over-month trend, top 5 services by cost, biggest cost increases, active Advisor savings opportunities, and reservation utilization. Include charts.",
      },
      {
        label: "Tagging gaps report",
        prompt:
          "Audit my resources for missing tags. Which resources are missing cost-center, environment, or owner tags? Group by resource group and show the monthly cost of untagged resources — I need to know the financial impact of poor tagging.",
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
        label: "Billing accounts",
        prompt:
          "Show my billing account structure — accounts, profiles, invoice sections.",
      },
      {
        label: "FinOps maturity scorecard",
        prompt:
          "Assess my FinOps maturity across key dimensions: Do I have budgets set? Are resources tagged? Am I using reservations/savings plans? What's my Advisor recommendation adoption rate? Am I tracking cost anomalies? Summarize strengths and gaps in a table and suggest next steps.",
      },
    ],
  },
  {
    key: "finops_pricing",
    label: "Pricing & Benchmarking",
    icon: "$",
    requiresAzure: false,
    prompts: [
      {
        label: "Regional cost map",
        prompt:
          "Show a world map of Azure regions color-coded by D4s_v5 VM pricing.",
      },
      {
        label: "VM pricing comparison",
        prompt:
          "Compare D4s_v5, D8s_v5, D16s_v5 monthly costs across East US, West Europe, Southeast Asia. Show a chart.",
      },
      {
        label: "GPU pricing",
        prompt:
          "Compare NC, ND, NV GPU VM pricing in East US. Best price per GPU?",
      },
      {
        label: "Database pricing",
        prompt:
          "Compare Cosmos DB serverless vs Azure SQL General Purpose for 10M req/day.",
      },
      {
        label: "Multi-region comparison",
        prompt:
          "Compare Azure VM pricing (D4s_v5) across all US regions. Show a ranked table with the cheapest region at the top and a bar chart of monthly costs.",
      },
      {
        label: "Service health",
        prompt:
          "Any active Azure service health incidents in East US, West Europe, Southeast Asia?",
      },
      {
        label: "Spot VM pricing",
        prompt:
          "Compare spot vs on-demand pricing for D4s_v5, D8s_v5, and D16s_v5 VMs in East US. Show the discount percentage and estimated monthly savings for each SKU in a table.",
      },
      {
        label: "Networking pricing",
        prompt:
          "Compare the monthly cost of Azure ExpressRoute (Standard vs Premium, metered vs unlimited) circuits across 50 Mbps, 200 Mbps, and 1 Gbps options. Show a table with all combinations.",
      },
    ],
  },
  {
    key: "finops_infra",
    label: "Infrastructure & Security",
    icon: "I",
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
        label: "Resource locks audit",
        prompt:
          "List all resource locks (CanNotDelete and ReadOnly) across my subscriptions. Are my critical production resources properly protected? Identify high-value resources that are missing locks.",
      },
      {
        label: "RBAC review",
        prompt:
          "List all Owner and Contributor role assignments across my subscriptions. Identify any direct user assignments (not via groups) and any assignments to external/guest accounts — these are security risks.",
      },
      {
        label: "Management groups",
        prompt:
          "Show my management group hierarchy with subscriptions under each.",
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
            case "pptx_ready": {
              pptxReady.value = {
                fileId: data.fileId,
                fileName: data.fileName,
                slideCount: data.slideCount,
              };
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

    const msgObj = {
      role: "assistant",
      content: streamBuffer.value,
      toolCalls: toolCalls.map((tc) => ({ ...tc, expanded: false })),
      charts: [...streamCharts.value],
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
  padding: 2rem 2rem 1.5rem;
  max-width: 720px;
  margin: 0 auto;
  width: 100%;
  animation: fadeSlideIn 0.4s ease;
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
  font-size: 2.2rem;
  font-weight: 800;
  margin: 0;
  letter-spacing: -0.03em;
  background: linear-gradient(135deg, #1f2328 25%, #0078d4 100%);
  -webkit-background-clip: text;
  -webkit-text-fill-color: transparent;
  background-clip: text;
}
.es-tagline {
  font-size: 0.85rem;
  font-weight: 600;
  color: #0078d4;
  margin: 0.2rem 0 0.6rem;
  letter-spacing: 0.01em;
}
.es-sub {
  font-size: 0.88rem;
  color: var(--text-muted);
  margin: 0 0 1rem;
  line-height: 1.55;
  text-align: center;
  max-width: 600px;
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
  background: #1f2328;
  color: #fff;
}
.login-btn--github:hover:not(:disabled) {
  background: #2da44e;
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
</style>
