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
      <div v-if="azureConnected && azureUserEmail" class="portal-header-right">
        <span class="portal-header-email">{{ azureUserEmail }}</span>
        <button
          class="portal-header-disconnect"
          @click="disconnectAzure"
          title="Disconnect Azure"
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
            <line x1="18" y1="6" x2="6" y2="18" />
            <line x1="6" y1="6" x2="18" y2="18" />
          </svg>
        </button>
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
          <!-- FinOps Maturity Categories (Crawl / Walk / Run + Pricing) -->
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
              <div class="sidebar-category-left">
                <span>{{ cat.label }}</span>
                <span v-if="cat.subtitle" class="sidebar-category-subtitle">{{
                  cat.subtitle
                }}</span>
              </div>
              <div class="sidebar-category-right">
                <span
                  v-if="cat.requiresAzure && maturityScores[cat.key]"
                  class="sidebar-stars"
                  :style="{ color: starColor(maturityOverall(cat.key)) }"
                  >{{ starsText(maturityOverall(cat.key)) }}</span
                >
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
            </div>
            <!-- Score results (from LLM) -->
            <div
              v-if="
                cat.requiresAzure &&
                maturityScores[cat.key] &&
                !collapsedSections[cat.key]
              "
              class="assessment-summary"
            >
              <div
                v-for="sc in maturityScores[cat.key]"
                :key="sc.id"
                class="assessment-row"
              >
                <span
                  class="assessment-stars"
                  :style="{ color: starColor(sc.score) }"
                  >{{ starsText(sc.score) }}</span
                >
                <span class="assessment-label">{{ sc.label }}</span>
                <span class="assessment-detail-text">{{ sc.detail }}</span>
              </div>
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
                  :class="{
                    'sidebar-question-label--score':
                      q.label.startsWith('Score '),
                  }"
                  >{{ q.label }}</span
                >
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
                :title="
                  sub.name +
                  '\n' +
                  sub.id +
                  (sub.tenantId ? '\nTenant: ' + sub.tenantId : '')
                "
              >
                <span class="sidebar-sub-name">{{ sub.name }}</span>
                <span class="sidebar-sub-id" :title="sub.id">{{ sub.id }}</span>
                <span
                  v-if="tenantNameFor(sub.tenantId)"
                  class="sidebar-sub-tenant"
                  :title="sub.tenantId"
                  >Tenant: {{ tenantNameFor(sub.tenantId) }}</span
                >
              </div>
            </div>
          </div>
        </div>

        <!-- Bottom section -->
        <div class="sidebar-footer">
          <!-- Azure connect/status -->
          <!-- Hide the sidebar Connect widget while the empty-state hero is showing
               its own Connect Azure CTA, to avoid a duplicate button. -->
          <div
            v-if="!azureConnected && messages.length > 0"
            class="azure-connect"
          >
            <!-- Admin approval / consent error banner -->
            <div v-if="tenantError" class="tenant-error-banner">
              <svg
                width="14"
                height="14"
                viewBox="0 0 24 24"
                fill="none"
                stroke="#d13438"
                stroke-width="2"
                stroke-linecap="round"
                stroke-linejoin="round"
              >
                <circle cx="12" cy="12" r="10" />
                <line x1="12" y1="8" x2="12" y2="12" />
                <line x1="12" y1="16" x2="12.01" y2="16" />
              </svg>
              <span
                >Your home tenant blocked this app. Pick a tenant below or type
                one manually.</span
              >
            </div>
            <!-- Saved tenants — hidden -->
            <div v-if="false" class="saved-tenants">
              <span class="saved-tenants-label">Your tenants</span>
              <button
                v-for="t in savedTenants"
                :key="t.tenantId"
                class="saved-tenant-btn"
                @click="switchTenant(t.tenantId)"
                :title="t.tenantId"
              >
                <svg
                  width="11"
                  height="11"
                  viewBox="0 0 24 24"
                  fill="none"
                  stroke="currentColor"
                  stroke-width="2"
                  stroke-linecap="round"
                  stroke-linejoin="round"
                >
                  <path d="M3 9l9-7 9 7v11a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z" />
                  <polyline points="9 22 9 12 15 12 15 22" />
                </svg>
                {{
                  t.displayName ||
                  t.defaultDomain ||
                  t.tenantId.slice(0, 8) + "…"
                }}
              </button>
            </div>
            <!-- Manual tenant input -->
            <div class="tenant-input-area">
              <input
                v-model="tenantId"
                type="text"
                class="tenant-input"
                :class="{
                  'tenant-input--highlight':
                    tenantError && !savedTenants.length,
                }"
                placeholder="Tenant ID…"
              />
              <span v-if="!savedTenants.length" class="tenant-hint"
                >Leave empty to use your home tenant, or specify a different
                one</span
              >
            </div>
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
              {{
                authLoading === "azure"
                  ? "Connecting..."
                  : tenantId.trim()
                    ? "Connect to " + tenantId.trim()
                    : "Connect Azure"
              }}
            </button>
          </div>
          <div v-else-if="azureConnected" class="azure-status">
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
            <!-- Tenant switcher -->
            <div class="tenant-switcher" v-if="availableTenants.length > 1">
              <label
                class="tenant-switch-label"
                @click="showTenantSwitcher = !showTenantSwitcher"
              >
                <svg
                  width="11"
                  height="11"
                  viewBox="0 0 24 24"
                  fill="none"
                  stroke="currentColor"
                  stroke-width="2"
                  stroke-linecap="round"
                  stroke-linejoin="round"
                >
                  <path d="M3 9l9-7 9 7v11a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z" />
                  <polyline points="9 22 9 12 15 12 15 22" />
                </svg>
                Switch tenant ({{ availableTenants.length }})
                <svg
                  :class="['tenant-chevron', { open: showTenantSwitcher }]"
                  width="10"
                  height="10"
                  viewBox="0 0 24 24"
                  fill="none"
                  stroke="currentColor"
                  stroke-width="2.5"
                  stroke-linecap="round"
                  stroke-linejoin="round"
                >
                  <polyline points="6 9 12 15 18 9" />
                </svg>
              </label>
              <div v-if="showTenantSwitcher" class="tenant-list">
                <button
                  v-for="t in availableTenants"
                  :key="t.tenantId"
                  class="tenant-list-item"
                  :class="{ active: t.tenantId === currentTenantId }"
                  :disabled="t.tenantId === currentTenantId"
                  @click="switchTenant(t.tenantId)"
                  :title="t.tenantId"
                >
                  <span class="tenant-list-name">{{
                    t.displayName || t.defaultDomain || t.tenantId
                  }}</span>
                  <span
                    v-if="t.tenantId === currentTenantId"
                    class="tenant-list-current"
                    >current</span
                  >
                  <span
                    v-if="t.defaultDomain && t.displayName"
                    class="tenant-list-domain"
                    >{{ t.defaultDomain }}</span
                  >
                </button>
              </div>
            </div>
            <!-- Incremental consent: one row per scope, all delegated, separate Entra ID consent each -->
            <div class="addons-section">
              <button
                class="addons-heading"
                type="button"
                @click="addonsOpen = !addonsOpen"
                :aria-expanded="addonsOpen"
              >
                <span class="addons-heading-text">
                  <span class="addons-title">Add scopes</span>
                  <span class="addons-sub">· Delegated &amp; read-only</span>
                </span>
                <svg
                  class="addons-heading-chevron"
                  :class="{ open: addonsOpen }"
                  width="14"
                  height="14"
                  viewBox="0 0 24 24"
                  fill="none"
                  stroke="currentColor"
                  stroke-width="2.5"
                  stroke-linecap="round"
                  stroke-linejoin="round"
                  aria-hidden="true"
                >
                  <polyline points="6 9 12 15 18 9" />
                </svg>
              </button>

              <div class="addons-body-wrap" :class="{ open: addonsOpen }">
                <div class="addons-body">
                  <div
                    class="scope-row"
                    :class="{
                      'scope-row--active': licensesEnabled,
                      'scope-row--glow': glowingRow === 0,
                      'scope-row--open': addonRowsOpen[0],
                    }"
                  >
                    <button
                      class="scope-row-summary"
                      type="button"
                      @click="clickScopeRow(0)"
                      title="Show details"
                    >
                      <span v-if="licensesEnabled" class="scope-row-mark"
                        >✓</span
                      >
                      <span class="scope-row-title">License Optimization</span>
                      <span
                        class="scope-row-chevron"
                        @click.stop="addonRowsOpen[0] = !addonRowsOpen[0]"
                        title="Show details"
                      >
                        <svg
                          width="14"
                          height="14"
                          viewBox="0 0 24 24"
                          fill="none"
                          stroke="currentColor"
                          stroke-width="2.5"
                          stroke-linecap="round"
                          stroke-linejoin="round"
                        >
                          <polyline points="6 9 12 15 18 9" />
                        </svg>
                      </span>
                    </button>
                    <div class="scope-row-detail-wrap">
                      <div class="scope-row-detail">
                        <p class="scope-row-desc">
                          Read M365 license inventory &amp; Copilot adoption
                        </p>
                        <div class="scope-row-meta">
                          <span class="scope-badge scope-badge--delegated"
                            >👤 Delegated</span
                          >
                          <span class="scope-badge">Microsoft Graph</span>
                        </div>
                        <p class="scope-row-perms">
                          Organization.Read.All · Reports.Read.All
                        </p>
                        <button
                          v-if="!licensesEnabled"
                          class="scope-row-add"
                          @click.stop="
                            startAuth('azure', '/auth/microsoft?tier=licenses')
                          "
                        >
                          Add scope
                        </button>
                        <span v-else class="scope-row-status">✓ Consented</span>
                      </div>
                    </div>
                  </div>

                  <div
                    class="scope-row"
                    :class="{
                      'scope-row--active': chargebackEnabled,
                      'scope-row--glow': glowingRow === 1,
                      'scope-row--open': addonRowsOpen[1],
                    }"
                  >
                    <button
                      class="scope-row-summary"
                      type="button"
                      @click="clickScopeRow(1)"
                      title="Show details"
                    >
                      <span v-if="chargebackEnabled" class="scope-row-mark"
                        >✓</span
                      >
                      <span class="scope-row-title"
                        >Cost Allocation &amp; Chargeback</span
                      >
                      <span
                        class="scope-row-chevron"
                        @click.stop="addonRowsOpen[1] = !addonRowsOpen[1]"
                        title="Show details"
                      >
                        <svg
                          width="14"
                          height="14"
                          viewBox="0 0 24 24"
                          fill="none"
                          stroke="currentColor"
                          stroke-width="2.5"
                          stroke-linecap="round"
                          stroke-linejoin="round"
                        >
                          <polyline points="6 9 12 15 18 9" />
                        </svg>
                      </span>
                    </button>
                    <div class="scope-row-detail-wrap">
                      <div class="scope-row-detail">
                        <p class="scope-row-desc">
                          Map Azure costs to users, teams &amp; cost centers
                        </p>
                        <div class="scope-row-meta">
                          <span class="scope-badge scope-badge--delegated"
                            >👤 Delegated</span
                          >
                          <span class="scope-badge">Microsoft Graph</span>
                        </div>
                        <p class="scope-row-perms">
                          User.Read.All · Group.Read.All
                        </p>
                        <button
                          v-if="!chargebackEnabled"
                          class="scope-row-add"
                          @click.stop="
                            startAuth(
                              'azure',
                              '/auth/microsoft?tier=chargeback',
                            )
                          "
                        >
                          Add scope
                        </button>
                        <span v-else class="scope-row-status">✓ Consented</span>
                      </div>
                    </div>
                  </div>

                  <div
                    class="scope-row"
                    :class="{
                      'scope-row--active': logAnalyticsEnabled,
                      'scope-row--glow': glowingRow === 2,
                      'scope-row--open': addonRowsOpen[2],
                    }"
                  >
                    <button
                      class="scope-row-summary"
                      type="button"
                      @click="clickScopeRow(2)"
                      title="Show details"
                    >
                      <span v-if="logAnalyticsEnabled" class="scope-row-mark"
                        >✓</span
                      >
                      <span class="scope-row-title"
                        >Log Analytics Deep Dives</span
                      >
                      <span
                        class="scope-row-chevron"
                        @click.stop="addonRowsOpen[2] = !addonRowsOpen[2]"
                        title="Show details"
                      >
                        <svg
                          width="14"
                          height="14"
                          viewBox="0 0 24 24"
                          fill="none"
                          stroke="currentColor"
                          stroke-width="2.5"
                          stroke-linecap="round"
                          stroke-linejoin="round"
                        >
                          <polyline points="6 9 12 15 18 9" />
                        </svg>
                      </span>
                    </button>
                    <div class="scope-row-detail-wrap">
                      <div class="scope-row-detail">
                        <p class="scope-row-desc">
                          Run KQL for unit economics &amp; ingestion cost
                          analysis
                        </p>
                        <div class="scope-row-meta">
                          <span class="scope-badge scope-badge--delegated"
                            >👤 Delegated</span
                          >
                          <span class="scope-badge">Log Analytics API</span>
                        </div>
                        <p class="scope-row-perms">Data.Read</p>
                        <button
                          v-if="!logAnalyticsEnabled"
                          class="scope-row-add"
                          @click.stop="
                            startAuth(
                              'azure',
                              '/auth/microsoft?tier=loganalytics',
                            )
                          "
                        >
                          Add scope
                        </button>
                        <span v-else class="scope-row-status">✓ Consented</span>
                      </div>
                    </div>
                  </div>

                  <div
                    class="scope-row"
                    :class="{
                      'scope-row--active': storageEnabled,
                      'scope-row--glow': glowingRow === 3,
                      'scope-row--open': addonRowsOpen[3],
                    }"
                  >
                    <button
                      class="scope-row-summary"
                      type="button"
                      @click="clickScopeRow(3)"
                      title="Show details"
                    >
                      <span v-if="storageEnabled" class="scope-row-mark"
                        >✓</span
                      >
                      <span class="scope-row-title">Cost Exports</span>
                      <span
                        class="scope-row-chevron"
                        @click.stop="addonRowsOpen[3] = !addonRowsOpen[3]"
                        title="Show details"
                      >
                        <svg
                          width="14"
                          height="14"
                          viewBox="0 0 24 24"
                          fill="none"
                          stroke="currentColor"
                          stroke-width="2.5"
                          stroke-linecap="round"
                          stroke-linejoin="round"
                        >
                          <polyline points="6 9 12 15 18 9" />
                        </svg>
                      </span>
                    </button>
                    <div class="scope-row-detail-wrap">
                      <div class="scope-row-detail">
                        <p class="scope-row-desc">
                          Read Cost Management export files from your Storage
                          Account
                        </p>
                        <div class="scope-row-meta">
                          <span class="scope-badge scope-badge--delegated"
                            >👤 Delegated</span
                          >
                          <span class="scope-badge">Azure Storage</span>
                        </div>
                        <p class="scope-row-perms">user_impersonation</p>
                        <button
                          v-if="!storageEnabled"
                          class="scope-row-add"
                          @click.stop="
                            startAuth('azure', '/auth/microsoft?tier=storage')
                          "
                        >
                          Add scope
                        </button>
                        <span v-if="storageEnabled" class="scope-row-status"
                          >✓ Consented</span
                        >
                      </div>
                    </div>
                  </div>

                  <div class="addons-divider"></div>

                  <button
                    v-if="
                      !(
                        licensesEnabled &&
                        chargebackEnabled &&
                        logAnalyticsEnabled &&
                        storageEnabled
                      )
                    "
                    class="scope-grant-all"
                    @click="startAuth('azure', '/auth/microsoft/adminconsent')"
                    title="Tenant admins (Global Admin / Privileged Role Admin): one click consents all 4 scopes for every user in your tenant. Non-admins will see a 'You need admin approval' message — use the individual scope buttons above instead."
                  >
                    <span class="scope-grant-all-icon">🛡</span>
                    <span class="scope-grant-all-body">
                      <span class="scope-grant-all-title"
                        >Grant for whole tenant
                        <span class="scope-grant-all-tag">admin</span></span
                      >
                      <span class="scope-grant-all-desc"
                        >Requires Global Admin · 1 click consents all 4 scopes
                        for every user.</span
                      >
                    </span>
                  </button>
                </div>
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
        </div>
      </aside>

      <!-- Center: chat area -->
      <div class="chat-main">
        <!-- Messages -->
        <div class="messages" ref="messagesEl">
          <div class="messages-inner">
            <div v-if="messages.length === 0" class="empty-state">
              <h1 class="es-headline">Azure FinOps Agent</h1>
              <p class="es-eyebrow">
                Built for FinOps leads, CCoE teams, and the architects who serve
                them.
              </p>
              <p class="es-sub">
                Replace a multi-week FinOps assessment with a single
                conversation. Connect a tenant, ask in plain language, and walk
                away with quantified savings, a FinOps Foundation maturity
                score, and a CFO-ready deck — in minutes, not sprints.
                <strong>Read-only by design</strong>, multi-tenant, and safe to
                point at anything from a dev sandbox to a global enterprise
                estate.
              </p>

              <div v-if="!azureConnected" class="es-connect-bar">
                <!-- Admin approval / consent error banner -->
                <div
                  v-if="tenantError"
                  class="tenant-error-banner es-tenant-error"
                >
                  <svg
                    width="16"
                    height="16"
                    viewBox="0 0 24 24"
                    fill="none"
                    stroke="#d13438"
                    stroke-width="2"
                    stroke-linecap="round"
                    stroke-linejoin="round"
                  >
                    <circle cx="12" cy="12" r="10" />
                    <line x1="12" y1="8" x2="12" y2="12" />
                    <line x1="12" y1="16" x2="12.01" y2="16" />
                  </svg>
                  <span
                    >Your home tenant requires admin approval. Pick a tenant
                    below or type one manually.</span
                  >
                </div>
                <!-- Saved tenants — hidden -->
                <div v-if="false" class="saved-tenants es-saved-tenants">
                  <span class="saved-tenants-label">Your tenants</span>
                  <div class="saved-tenants-list">
                    <button
                      v-for="t in savedTenants"
                      :key="t.tenantId"
                      class="saved-tenant-btn"
                      @click="switchTenant(t.tenantId)"
                      :title="t.tenantId"
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
                        <path
                          d="M3 9l9-7 9 7v11a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z"
                        />
                        <polyline points="9 22 9 12 15 12 15 22" />
                      </svg>
                      {{
                        t.displayName ||
                        t.defaultDomain ||
                        t.tenantId.slice(0, 8) + "…"
                      }}
                    </button>
                  </div>
                </div>
                <div class="es-tenant-input-row">
                  <input
                    v-model="tenantId"
                    type="text"
                    class="tenant-input es-tenant-input"
                    :class="{
                      'tenant-input--highlight':
                        tenantError && !savedTenants.length,
                    }"
                    placeholder="Tenant ID…"
                  />
                </div>
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
                      : tenantId.trim()
                        ? "Connect to " + tenantId.trim()
                        : "Connect Azure"
                  }}
                </button>
              </div>

              <!-- Capabilities -->
              <div class="es-capabilities">
                <div class="es-capabilities-grid">
                  <div class="es-cap-item">
                    <div class="es-cap-title">
                      Quantified savings, ranked by $ impact
                    </div>
                    <div class="es-cap-desc">
                      Reservations, Savings Plans, Hybrid Benefit, rightsizing,
                      idle and orphaned resources — surfaced with utilization
                      evidence and an estimated annual saving, so you act on the
                      biggest levers first.
                    </div>
                  </div>
                  <div class="es-cap-item">
                    <div class="es-cap-title">
                      FinOps Foundation maturity score
                    </div>
                    <div class="es-cap-desc">
                      Grades your tenant against the Crawl / Walk / Run
                      framework with a 0–5 score per capability and a
                      prioritized roadmap — the assessment a consultant bills
                      weeks for, delivered in a chat.
                    </div>
                  </div>
                  <div class="es-cap-item">
                    <div class="es-cap-title">
                      Agentic reasoning, not a dashboard
                    </div>
                    <div class="es-cap-desc">
                      Plans the investigation, picks the right ARM / Graph / Log
                      Analytics calls, runs Python (pandas, numpy)
                      mid-conversation, and revises when the data surprises it —
                      autonomy a static report can't match.
                    </div>
                  </div>
                  <div class="es-cap-item">
                    <div class="es-cap-title">
                      Anomalies, chargeback &amp; tag hygiene
                    </div>
                    <div class="es-cap-desc">
                      Catches cost spikes and explains the <em>why</em> —
                      resource, change, owner. Auto-generates showback by tag or
                      business unit and quantifies the untagged spend breaking
                      accountability.
                    </div>
                  </div>
                  <div class="es-cap-item">
                    <div class="es-cap-title">
                      M365 license &amp; Copilot ROI
                    </div>
                    <div class="es-cap-desc">
                      Microsoft Graph integration surfaces unused licenses,
                      Copilot seat utilization, and SKU mismatch — levers Azure
                      Cost Management simply can't see, across 40+ Azure
                      services and every subscription in one query.
                    </div>
                  </div>
                  <div class="es-cap-item">
                    <div class="es-cap-title">
                      Inline charts + CFO-ready PowerPoint
                    </div>
                    <div class="es-cap-desc">
                      20+ ECharts visualizations (treemaps, heatmaps, world
                      maps, sankey) plus a one-click branded .pptx export — walk
                      into the steering committee with the deck already built.
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
                  <div v-if="msg.script" class="script-inline-block">
                    <div class="script-header">
                      <div class="script-header-left">
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
                          <polyline points="16 18 22 12 16 6" />
                          <polyline points="8 6 2 12 8 18" />
                        </svg>
                        <span class="script-filename">{{
                          msg.script.fileName
                        }}</span>
                        <span class="script-meta"
                          >{{ msg.script.lineCount }} lines &middot;
                          {{
                            msg.script.language === "powershell"
                              ? "PowerShell"
                              : "Bash"
                          }}</span
                        >
                      </div>
                      <div class="script-header-actions">
                        <button
                          class="script-copy-btn"
                          @click="copyScript(msg.script.content)"
                          :title="'Copy to clipboard'"
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
                            <rect
                              x="9"
                              y="9"
                              width="13"
                              height="13"
                              rx="2"
                              ry="2"
                            />
                            <path
                              d="M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1"
                            />
                          </svg>
                        </button>
                        <a
                          :href="'/api/download/script/' + msg.script.fileId"
                          class="script-download-btn"
                          download
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
                              d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"
                            />
                            <polyline points="7 10 12 15 17 10" />
                            <line x1="12" y1="15" x2="12" y2="3" />
                          </svg>
                          Download
                        </a>
                      </div>
                    </div>
                    <div
                      class="script-description"
                      v-if="msg.script.description"
                    >
                      {{ msg.script.description }}
                    </div>
                    <pre
                      class="script-code"
                    ><code>{{ msg.script.content }}</code></pre>
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

        <!-- Script download (streaming) -->
        <div v-if="scriptReady" class="script-download-bar">
          <div class="script-inline-block">
            <div class="script-header">
              <div class="script-header-left">
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
                  <polyline points="16 18 22 12 16 6" />
                  <polyline points="8 6 2 12 8 18" />
                </svg>
                <span class="script-filename">{{ scriptReady.fileName }}</span>
                <span class="script-meta"
                  >{{ scriptReady.lineCount }} lines &middot;
                  {{
                    scriptReady.language === "powershell"
                      ? "PowerShell"
                      : "Bash"
                  }}</span
                >
              </div>
              <div class="script-header-actions">
                <button
                  class="script-copy-btn"
                  @click="copyScript(scriptReady.content)"
                  title="Copy to clipboard"
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
                    <rect x="9" y="9" width="13" height="13" rx="2" ry="2" />
                    <path
                      d="M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1"
                    />
                  </svg>
                </button>
                <a
                  :href="'/api/download/script/' + scriptReady.fileId"
                  class="script-download-btn"
                  download
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
                    <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4" />
                    <polyline points="7 10 12 15 17 10" />
                    <line x1="12" y1="15" x2="12" y2="3" />
                  </svg>
                  Download
                </a>
              </div>
            </div>
            <div class="script-description" v-if="scriptReady.description">
              {{ scriptReady.description }}
            </div>
            <pre
              class="script-code"
            ><code>{{ scriptReady.content }}</code></pre>
          </div>
        </div>

        <!-- Mobile auth bar (hidden on desktop, shown on mobile) -->
        <div class="mobile-auth-bar"></div>

        <!-- Input bar -->
        <div class="input-area">
          <div
            class="input-wrapper"
            :class="{ 'input-wrapper--disabled': false }"
          >
            <textarea
              ref="inputEl"
              v-model="input"
              rows="1"
              @keydown.enter.exact.prevent="send"
              @input="autoGrowInput"
              placeholder="Ask a question about your data"
              class="input-field"
              :disabled="!user"
            ></textarea>
            <div class="input-bottom-bar">
              <div class="input-bottom-left">
                <button
                  class="input-action-btn"
                  :disabled="messages.length === 0 || streaming"
                  @click="clearMessages()"
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
                    <path
                      d="M3 12a9 9 0 1 0 9-9 9.75 9.75 0 0 0-6.74 2.74L3 8"
                    />
                    <path d="M3 3v5h5" />
                  </svg>
                  <span>Clear</span>
                </button>
                <button
                  class="input-action-btn"
                  :disabled="messages.length < 2 || streaming"
                  @click="requestPresentation()"
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
                  <span>PowerPoint</span>
                </button>
                <button
                  class="input-action-btn"
                  :disabled="messages.length < 2 || streaming"
                  @click="requestScript()"
                  title="Generate Script"
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
                    <polyline points="16 18 22 12 16 6" />
                    <polyline points="8 6 2 12 8 18" />
                  </svg>
                  <span>Script</span>
                </button>
              </div>
              <div class="input-bottom-right">
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
import {
  maturityCategories,
  pricingCategory,
} from "../data/sidebarCategories.js";

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
const scriptReady = ref(null);
const messagesEl = ref(null);
const inputEl = ref(null);
const chartInstances = [];
let intentAnimTimer = null;

function autoGrowInput() {
  const el = inputEl.value;
  if (!el) return;
  el.style.height = "auto";
  el.style.height = Math.min(el.scrollHeight, 400) + "px";
  el.style.overflowY = el.scrollHeight > 400 ? "auto" : "hidden";
}

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
  crawl: true,
  walk: true,
  run: true,
  pricing: true,
});
function toggleSection(key) {
  collapsedSections[key] = !collapsedSections[key];
}
const buildSha = ref("");
const buildNumber = ref("0");
const sidebarOpen = ref(
  typeof window !== "undefined" ? window.innerWidth > 768 : true,
);
const plusMenuOpen = ref(false);
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
const storageEnabled = ref(false);
const tenantId = ref(""); // User-specified tenant ID or domain for guest users
const availableTenants = ref([]);
const currentTenantId = ref("");
const showTenantSwitcher = ref(false);
const tenantError = ref(false);
const clearing = ref(false);

// Add-ons collapsible parent + per-row open state + onboarding glow tour
// Defaults to collapsed; the first-login tour expands → animates each row → collapses again.
const addonsOpen = ref(false);
const addonRowsOpen = ref([false, false, false, false]);
const glowingRow = ref(-1);

// One-time onboarding tour: when user first connects, briefly open each
// add-on row (with a glow) so they discover the click-to-expand UI, then
// collapse the whole parent so only the email + "Add scopes" toggle remain.
// Runs once per browser (localStorage). To force-replay: localStorage.removeItem('addons-tour-shown')
async function runAddonsTour() {
  if (localStorage.getItem("addons-tour-shown")) return;
  await new Promise((r) => setTimeout(r, 900));
  addonsOpen.value = true;
  for (let i = 0; i < 4; i++) {
    glowingRow.value = i;
    addonRowsOpen.value[i] = true;
    await new Promise((r) => setTimeout(r, 1400));
    addonRowsOpen.value[i] = false;
    glowingRow.value = -1;
    await new Promise((r) => setTimeout(r, 300));
  }
  // Final beat — let the user see the full collapsed list, then minimise
  // the parent so the sidebar shows just the email + "Add scopes" toggle.
  await new Promise((r) => setTimeout(r, 700));
  addonsOpen.value = false;
  // Mark as shown only after the tour completes successfully so partial
  // runs (e.g. user navigates away mid-tour) replay on next visit.
  localStorage.setItem("addons-tour-shown", "1");
}

// Click on a scope row: just toggle the details panel.
// Adding the scope is done via the explicit "Add scope" button inside.
function clickScopeRow(idx) {
  addonRowsOpen.value[idx] = !addonRowsOpen.value[idx];
}

// Resolve a tenant GUID to a friendly display name from availableTenants.
function tenantNameFor(tenantId) {
  if (!tenantId) return "";
  const t = availableTenants.value.find((x) => x.tenantId === tenantId);
  return t ? t.displayName || t.defaultDomain || "" : "";
}

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
        storageEnabled.value = data.storageEnabled || false;
        // Fetch tenant list after connecting
        fetchTenants();
        // First-time onboarding: glow-tour the add-on rows
        runAddonsTour();
      }
    }
  } catch {}
}

async function fetchTenants() {
  try {
    const r = await fetch("/auth/azure/tenants");
    if (r.ok) {
      const data = await r.json();
      availableTenants.value = data.tenants || [];
      currentTenantId.value = data.currentTenantId || "";
      // Persist tenants to localStorage so they appear as clickable buttons
      // on future visits — even before connecting
      if (availableTenants.value.length > 0) {
        localStorage.setItem(
          "knownTenants",
          JSON.stringify(availableTenants.value),
        );
      }
    }
  } catch {}
}

// Load previously discovered tenants from localStorage
const savedTenants = computed(() => {
  try {
    return JSON.parse(localStorage.getItem("knownTenants") || "[]");
  } catch {
    return [];
  }
});

function switchTenant(tid) {
  startAuth("azure", "/auth/microsoft?tenant=" + encodeURIComponent(tid));
}

function startAuth(provider, url) {
  authLoading.value = provider;
  sessionStorage.setItem("authLoading", provider);
  // Append tenant param if user specified one (for guest users with multiple tenants)
  let authUrl = url;
  if (tenantId.value.trim()) {
    const sep = url.includes("?") ? "&" : "?";
    authUrl += sep + "tenant=" + encodeURIComponent(tenantId.value.trim());
  }
  setTimeout(() => {
    window.location.href = authUrl;
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
    storageEnabled.value = false;
    maturityScores.crawl = null;
    maturityScores.walk = null;
    maturityScores.run = null;
    maturityScores.playbook = null;
    // Reset onboarding so a fresh reconnect replays the add-ons tour.
    localStorage.removeItem("addons-tour-shown");
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
    storageEnabled.value = false;
    // Reset onboarding so the next reconnect replays the add-ons tour.
    localStorage.removeItem("addons-tour-shown");
    await clearMessages();
  } catch {}
}

// When Azure connects, expand Crawl and collapse pricing
watch(azureConnected, async (connected, wasConnected) => {
  if (!connected) return;
  collapsedSections.pricing = true;
  collapsedSections.crawl = true;
  collapsedSections.walk = true;
  collapsedSections.run = true;
  collapsedSections.playbook = true;
  // Auto-clear chat when Azure connects — removes stale "Connect Azure first" messages
  // and resets the Copilot session so the LLM knows the user is now connected
  if (!wasConnected) await clearMessages();
});

// Reset Copilot session when addon tiers are enabled so LLM picks up new tokens
watch(graphEnabled, async (enabled, was) => {
  if (enabled && !was) {
    await clearMessages();
  }
});
watch(logAnalyticsEnabled, async (enabled, was) => {
  if (enabled && !was) {
    await clearMessages();
  }
});
watch(storageEnabled, async (enabled, was) => {
  if (enabled && !was) {
    await clearMessages();
  }
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
    // Show tenant picker with error banner — the user's home tenant likely blocked the app
    tenantError.value = true;
    window.history.replaceState({}, "", window.location.pathname);
  }
  // Handle admin consent success — chain through each tier silently to populate
  // session tokens. Each redirect is silent because admin pre-approved everything.
  const adminConsentOk = params.get("admin_consent");
  if (adminConsentOk === "ok") {
    window.history.replaceState({}, "", window.location.pathname);
    sessionStorage.setItem("authLoading", "azure");
    authLoading.value = "azure";
    // Kick off silent token acquisition for the first add-on tier — the
    // existing tier-based flow handles the rest as the user clicks each.
    // Fastest UX: redirect to licenses tier first; user will see brief loading then refresh
    setTimeout(() => {
      window.location.href = "/auth/microsoft?tier=licenses&postadmin=1";
    }, 400);
    return;
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
  // Prevent concurrent send() while resetting
  clearing.value = true;
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
  scriptReady.value = null;
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
  clearing.value = false;
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
// Three maturity levels (Crawl/Walk/Run) when Azure connected, plus public pricing
const visibleCategories = computed(() =>
  azureConnected.value ? maturityCategories : [pricingCategory],
);

// ── Maturity scores (set by LLM via ReportMaturityScore tool → SSE) ──
const maturityScores = reactive({
  crawl: null, // null = not scored, array of {id, label, score, detail} when scored
  walk: null,
  run: null,
  playbook: null,
});

function maturityOverall(level) {
  const scores = maturityScores[level];
  if (!scores || scores.length === 0) return -1;
  return Math.round(
    scores.reduce((sum, s) => sum + s.score, 0) / scores.length,
  );
}

function starsText(score) {
  if (score < 0) return "☆☆☆☆☆";
  const full = Math.min(score, 5);
  return "★".repeat(full) + "☆".repeat(5 - full);
}

function starColor(score) {
  if (score >= 4) return "#107c10";
  if (score >= 3) return "#ff8c00";
  if (score >= 1) return "#d83b01";
  return "#a19f9d";
}

// Sidebar categories (FinOps maturity Crawl/Walk/Run/Playbook + Pricing) — see data/sidebarCategories.js

function sendQuestion(q) {
  if (streaming.value || clearing.value || !props.user) return;
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
  if (!props.user || clearing.value) return;
  input.value = text;
  send();
}

function requestPresentation() {
  input.value =
    "Generate a FinOps presentation from our conversation findings. Suggest a slide structure with the data we've discussed, and ask me if I want to customize anything before generating.";
  send();
}

function requestScript() {
  input.value =
    "Based on our conversation, generate an Azure CLI script to implement the FinOps recommendations we discussed. Ask me to confirm the specific actions before generating the script. If there are no actionable recommendations yet, let me know.";
  send();
}

function copyScript(content) {
  navigator.clipboard.writeText(content).catch(() => {});
}

async function send() {
  if (!props.user || clearing.value) return;
  const prompt = input.value.trim();
  if (!prompt || streaming.value) return;

  messages.value.push({ role: "user", content: prompt });
  input.value = "";
  nextTick(() => {
    if (inputEl.value) inputEl.value.style.height = "auto";
  });
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

          case "script_ready":
            scriptReady.value = {
              fileId: data.fileId,
              fileName: data.fileName,
              lineCount: data.lineCount,
              language: data.language,
              description: data.description,
              content: data.content,
            };
            scrollToBottom();
            break;

          case "maturity_score":
            try {
              const level = data.level?.toLowerCase();
              if (
                level &&
                (level === "crawl" ||
                  level === "walk" ||
                  level === "run" ||
                  level === "playbook")
              ) {
                const scores =
                  typeof data.scores === "string"
                    ? JSON.parse(data.scores)
                    : data.scores;
                if (Array.isArray(scores)) {
                  maturityScores[level] = scores;
                }
              }
            } catch {}
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
    if (scriptReady.value) {
      msgObj.script = { ...scriptReady.value };
      scriptReady.value = null;
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
    scriptReady.value = null;
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
/*
 * ══════════════════════════════════════════════════════════
 * Design tokens (Fluent / Azure Portal)
 *
 * Text:       #323130 (primary), #605e5c (secondary)
 * Accent:     #0078d4 (hover: #106ebe)
 * Borders:    #e1dfdd
 * Hover bg:   #f3f2f1
 * Surfaces:   #fff
 * Font:       14px "Segoe UI" base, 13px body text
 * Radius:     4px (controls), 8px (cards/panels)
 * ══════════════════════════════════════════════════════════
 */

/* ── Layout shell ── */
.chat-view {
  display: flex;
  flex-direction: column;
  height: 100%;
  min-height: 0;
  overflow: hidden;
}
.portal-body {
  display: flex;
  flex-direction: row;
  flex: 1;
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
  color: #fff;
}
.portal-readonly-badge {
  font-size: 10px;
  font-weight: 500;
  color: rgba(255, 255, 255, 0.85);
  background: rgba(255, 255, 255, 0.1);
  border: 1px solid rgba(255, 255, 255, 0.2);
  border-radius: 3px;
  padding: 1px 6px;
  margin-left: 8px;
  letter-spacing: 0.3px;
  cursor: default;
}
.portal-header-right {
  display: flex;
  align-items: center;
  gap: 8px;
}
.portal-header-email {
  font-size: 14px;
  font-weight: 600;
  color: #fff;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  max-width: 220px;
}
.portal-header-disconnect {
  background: none;
  border: none;
  color: rgba(255, 255, 255, 0.8);
  cursor: pointer;
  padding: 4px;
  border-radius: 4px;
  display: flex;
  align-items: center;
  justify-content: center;
  transition:
    background 0.15s,
    color 0.15s;
}
.portal-header-disconnect:hover {
  background: rgba(255, 255, 255, 0.15);
  color: #fff;
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
  font-size: 13px;
  font-weight: 400;
  color: #fff;
  max-width: 200px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.portal-user-tenant {
  font-size: 11px;
  font-weight: 400;
  color: rgba(255, 255, 255, 0.7);
  text-transform: uppercase;
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

/* ── Left sidebar ── */
.sidebar {
  width: 290px;
  flex-shrink: 0;
  border-right: 1px solid #e1dfdd;
  background: #fff;
  display: flex;
  flex-direction: column;
  overflow: hidden;
  transition:
    width 0.25s ease,
    opacity 0.2s ease;
}
.sidebar--collapsed {
  width: 0;
  opacity: 0;
  border-right: none;
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
  font-size: 14px;
  font-weight: 600;
  text-transform: none;
  letter-spacing: 0;
  color: #323130;
  margin-bottom: 4px;
  padding: 0 16px;
}
.sidebar-category--border {
  margin-top: 4px;
  border-top: 1px solid #e1dfdd;
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
    max-height 0.3s ease,
    opacity 0.25s ease;
}
.collapse-body--collapsed {
  max-height: 0;
  opacity: 0;
  transition:
    max-height 0.25s ease,
    opacity 0.15s ease;
}
.sidebar-question {
  display: flex;
  align-items: center;
  width: calc(100% - 20px);
  margin: 2px 10px;
  padding: 8px 12px;
  border: 1px solid #edebe9;
  border-radius: 6px;
  background: #fff;
  font-size: 13px;
  font-weight: 400;
  color: #323130;
  cursor: pointer;
  text-align: left;
  line-height: 1.4;
  transition:
    border-color 0.15s,
    background 0.15s,
    box-shadow 0.15s;
  font-family: inherit;
}
.sidebar-question:hover:not(:disabled) {
  border-color: #0078d4;
  background: #f3f2f1;
  color: #0078d4;
  box-shadow: 0 1px 3px rgba(0, 0, 0, 0.06);
}
.sidebar-question:disabled {
  opacity: 0.4;
  cursor: default;
}
.sidebar-question-label--score {
  font-weight: 600;
  font-size: 13px;
}
.sidebar-question--locked {
  opacity: 0.4;
}
.sidebar-question--locked:hover {
  background: transparent;
  color: #323130;
}

/* ── Category header layout ── */
.sidebar-category-label {
  display: flex;
  align-items: center;
  justify-content: space-between;
}
.sidebar-category-left {
  display: flex;
  flex-direction: column;
  gap: 1px;
}
.sidebar-category-subtitle {
  font-size: 10px;
  font-weight: 400;
  color: #a19f9d;
  letter-spacing: 0.2px;
}
.sidebar-category-right {
  display: flex;
  align-items: center;
  gap: 6px;
}
.sidebar-stars {
  font-size: 11px;
  letter-spacing: 1px;
}

/* ── Assessment summary rows ── */
.assessment-summary {
  padding: 4px 12px 6px;
  border-bottom: 1px solid #edebe9;
  background: #faf9f8;
}
.assessment-row {
  display: flex;
  align-items: baseline;
  gap: 6px;
  padding: 3px 4px;
  font-size: 11px;
  flex-wrap: wrap;
}
.assessment-stars {
  font-size: 10px;
  letter-spacing: 0.5px;
  flex-shrink: 0;
}
.assessment-label {
  flex: 1;
  color: #323130;
  font-weight: 500;
}
.assessment-detail-text {
  width: 100%;
  font-size: 10px;
  color: #a19f9d;
  padding-left: 0;
  line-height: 1.3;
  margin-top: -1px;
}
.sidebar-source {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 5px 16px;
  font-size: 13px;
  color: #323130;
}
.sidebar-source-dot {
  width: 7px;
  height: 7px;
  border-radius: 50%;
  flex-shrink: 0;
  background: #107c10;
  box-shadow: 0 0 4px rgba(16, 124, 16, 0.4);
}
.sidebar-source-dot--azure {
  background: #0078d4;
  box-shadow: 0 0 4px rgba(0, 120, 212, 0.4);
}
.sidebar-source-divider {
  font-size: 11px;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.04em;
  color: #605e5c;
  padding: 6px 16px 2px;
}
.sidebar-sub {
  display: flex;
  flex-direction: column;
  padding: 8px 16px;
  gap: 2px;
  border-bottom: 1px solid #f3f2f1;
}
.sidebar-sub:last-child {
  border-bottom: none;
}
.sidebar-sub-name {
  font-size: 13px;
  font-weight: 600;
  color: #201f1e;
  line-height: 1.3;
  word-break: break-word;
}
.sidebar-sub-id {
  font-size: 11px;
  color: #605e5c;
  font-family: "Cascadia Code", "Fira Code", Consolas, monospace;
  word-break: break-all;
  line-height: 1.35;
}
.sidebar-sub-tenant {
  font-size: 11px;
  color: #8a8886;
  line-height: 1.3;
  word-break: break-word;
}
.sidebar-footer {
  flex-shrink: 0;
  padding: 10px 14px;
  border-top: 1px solid #e1dfdd;
  display: flex;
  flex-direction: column;
  gap: 8px;
  background: #fff;
}

/* ── Azure connect / status (sidebar footer) ── */
.tenant-error-banner {
  display: flex;
  align-items: flex-start;
  gap: 8px;
  padding: 8px 10px;
  border-radius: 4px;
  background: #fef0f1;
  border: 1px solid #f3d6d8;
  font-size: 11.5px;
  color: #a4262c;
  line-height: 1.4;
  margin-bottom: 6px;
}
.tenant-error-banner svg {
  flex-shrink: 0;
  margin-top: 1px;
}
.es-tenant-error {
  max-width: 400px;
  margin: 0 auto 8px;
}
.tenant-input-area {
  margin-bottom: 6px;
}
.tenant-input {
  width: 100%;
  padding: 6px 10px;
  border-radius: 4px;
  border: 1px solid #e1dfdd;
  font-size: 12px;
  color: #323130;
  background: #faf9f8;
  outline: none;
  box-sizing: border-box;
  transition:
    border-color 0.15s,
    box-shadow 0.15s;
}
.tenant-input::placeholder {
  color: #a19f9d;
}
.tenant-input:focus {
  border-color: #0078d4;
  background: #fff;
  box-shadow: 0 0 0 1px #0078d4;
}
.tenant-input--highlight {
  border-color: #0078d4;
  background: #fff;
  box-shadow: 0 0 0 1px #0078d4;
}
.tenant-hint {
  display: block;
  font-size: 10.5px;
  color: #605e5c;
  margin-top: 3px;
}
.es-tenant-input-row {
  width: 100%;
  max-width: 360px;
}
.es-tenant-input {
  max-width: 360px;
  text-align: center;
}
.azure-connect-btn {
  display: flex;
  align-items: center;
  gap: 8px;
  width: 100%;
  padding: 8px 14px;
  border-radius: 4px;
  border: 1px solid #e1dfdd;
  background: #fff;
  color: #323130;
  font: inherit;
  font-size: 13px;
  font-weight: 600;
  cursor: pointer;
  transition: all 0.15s;
  justify-content: center;
}
.azure-connect-btn:hover {
  border-color: #0078d4;
  color: #0078d4;
  background: #f3f2f1;
}
.saved-tenants {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 6px;
  margin-bottom: 4px;
}
.saved-tenants-label {
  font-size: 11px;
  color: #8a8886;
  margin-right: 2px;
}
.saved-tenant-btn {
  display: inline-flex;
  align-items: center;
  gap: 5px;
  padding: 5px 12px;
  border-radius: 16px;
  border: 1px solid #e1dfdd;
  background: #f3f2f1;
  color: #323130;
  font-size: 12px;
  font-weight: 500;
  cursor: pointer;
  transition: all 0.15s;
}
.saved-tenant-btn:hover {
  border-color: #0078d4;
  color: #0078d4;
  background: #deecf9;
}
.azure-connect-hint {
  display: block;
  font-size: 11px;
  color: #8a8886;
  text-align: center;
  margin-top: 4px;
}
.azure-status {
  padding: 2px 0;
}
.azure-status-info {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 6px 10px;
  border-radius: 4px;
  background: #f3f2f1;
  border: 1px solid #e1dfdd;
}
.azure-status-text {
  flex: 1;
  font-size: 13px;
  color: #323130;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
.azure-disconnect-btn {
  background: none;
  border: none;
  color: #605e5c;
  cursor: pointer;
  padding: 2px;
  border-radius: 4px;
  display: flex;
  align-items: center;
}
.azure-disconnect-btn:hover {
  color: #d13438;
}
/* ── Tenant switcher ── */
.tenant-switcher {
  margin-top: 6px;
}
.tenant-switch-label {
  display: flex;
  align-items: center;
  gap: 5px;
  font-size: 11.5px;
  color: #0078d4;
  cursor: pointer;
  user-select: none;
  padding: 2px 0;
}
.tenant-switch-label:hover {
  color: #106ebe;
}
.tenant-list {
  margin-top: 4px;
  max-height: 160px;
  overflow-y: auto;
  border: 1px solid #e1dfdd;
  border-radius: 4px;
  background: #faf9f8;
}
.tenant-list-item {
  display: flex;
  align-items: center;
  gap: 6px;
  width: 100%;
  padding: 6px 10px;
  border: none;
  background: transparent;
  color: #323130;
  font-size: 12px;
  text-align: left;
  cursor: pointer;
  transition: background 0.1s;
}
.tenant-list-item:hover:not(.active) {
  background: #edebe9;
}
.tenant-list-item.active {
  background: #e6f2fb;
  cursor: default;
}
.tenant-list-item + .tenant-list-item {
  border-top: 1px solid #edebe9;
}
.tenant-list-name {
  flex: 1;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.tenant-list-current {
  font-size: 10px;
  color: #0078d4;
  font-weight: 600;
  text-transform: uppercase;
  flex-shrink: 0;
}
.tenant-list-domain {
  font-size: 10px;
  color: #8a8886;
  flex-shrink: 0;
}
/* ── Add-on scopes (delegated, incremental consent) ── */
.addons-section {
  display: flex;
  flex-direction: column;
  gap: 6px;
  margin-top: 10px;
}
.addons-heading {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
  padding: 8px 12px;
  margin-bottom: 2px;
  background: none;
  border: none;
  cursor: pointer;
  width: 100%;
  text-align: left;
  border-radius: 4px;
  transition: background 0.15s;
}
.addons-heading:hover {
  background: #f3f2f1;
}
.addons-heading-text {
  display: flex;
  align-items: baseline;
  gap: 6px;
  min-width: 0;
  flex-wrap: wrap;
}
.addons-heading-chevron {
  flex-shrink: 0;
  color: #605e5c;
  margin-left: 8px;
  transition: transform 0.4s cubic-bezier(0.4, 0, 0.2, 1);
}
.addons-heading-chevron.open {
  transform: rotate(180deg);
}
.addons-body-wrap {
  max-height: 0;
  overflow: hidden;
  opacity: 0;
  transition:
    max-height 0.6s cubic-bezier(0.4, 0, 0.2, 1),
    opacity 0.45s ease;
}
.addons-body-wrap.open {
  max-height: 1200px;
  opacity: 1;
}
.addons-body {
  display: flex;
  flex-direction: column;
  gap: 6px;
}
.addons-title {
  font-size: 11px;
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 0.4px;
  color: #605e5c;
}
.addons-sub {
  font-size: 11px;
  color: #8a8886;
  line-height: 1.35;
}
.addons-sub strong {
  color: #323130;
  font-weight: 600;
}
.scope-row {
  border-radius: 6px;
  border: 1px solid #e1dfdd;
  background: #fff;
  transition:
    border-color 0.15s,
    background 0.15s;
  overflow: hidden;
}
.scope-row--open {
  border-color: #c7c6c4;
  background: #fafafa;
}
.scope-row--active {
  background: #f3faf3;
  border-color: #cce8cc;
}
/* Onboarding glow tour — pulses each row briefly so user sees it's expandable */
.scope-row--glow {
  animation: scope-glow 0.75s ease-in-out;
}
@keyframes scope-glow {
  0% {
    box-shadow: 0 0 0 0 rgba(0, 120, 212, 0);
    border-color: #e1dfdd;
  }
  40% {
    box-shadow: 0 0 0 4px rgba(0, 120, 212, 0.25);
    border-color: #0078d4;
  }
  100% {
    box-shadow: 0 0 0 0 rgba(0, 120, 212, 0);
    border-color: #e1dfdd;
  }
}
.scope-row-summary {
  display: flex;
  align-items: stretch;
  gap: 8px;
  padding: 0 0 0 9px;
  cursor: pointer;
  width: 100%;
  background: none;
  border: none;
  text-align: left;
  font: inherit;
  color: inherit;
  transition: background 0.15s;
  min-height: 36px;
}
.scope-row-summary:hover {
  background: #f5fbff;
}
.scope-row--open .scope-row-summary:hover {
  background: #f0f0f0;
}
.scope-row-mark {
  flex-shrink: 0;
  align-self: center;
  width: 18px;
  height: 18px;
  border-radius: 50%;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  font-weight: 700;
  font-size: 12px;
  background: #f3f2f1;
  color: #605e5c;
}
.scope-row--active .scope-row-mark {
  background: #107c10;
  color: #fff;
}
.scope-row-title {
  font-size: 13px;
  font-weight: 600;
  color: #201f1e;
  line-height: 1.25;
  flex: 1;
  min-width: 0;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  align-self: center;
}
.scope-row-chevron {
  flex-shrink: 0;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 32px;
  align-self: stretch;
  color: #8a8886;
  transition: transform 0.4s cubic-bezier(0.4, 0, 0.2, 1);
  pointer-events: none;
}
.scope-row--open .scope-row-chevron {
  transform: rotate(180deg);
}
.scope-row-detail-wrap {
  max-height: 0;
  overflow: hidden;
  transition: max-height 0.5s cubic-bezier(0.4, 0, 0.2, 1);
}
.scope-row--open .scope-row-detail-wrap {
  max-height: 280px;
}
.scope-row-detail {
  padding: 4px 9px 9px 35px;
  display: flex;
  flex-direction: column;
  gap: 6px;
}
.scope-row-desc {
  font-size: 11.5px;
  color: #605e5c;
  line-height: 1.35;
  margin: 0;
}
.scope-row-meta {
  display: flex;
  flex-wrap: wrap;
  gap: 4px;
}
.scope-badge {
  display: inline-flex;
  align-items: center;
  padding: 1px 6px;
  border-radius: 3px;
  background: #f3f2f1;
  color: #605e5c;
  font-size: 10px;
  font-weight: 500;
  line-height: 1.5;
}
.scope-badge--delegated {
  background: #e0f2ff;
  color: #005a9e;
}
/* Compact summary-only delegated chip — just the icon */
.scope-row-summary > .scope-badge--delegated {
  padding: 1px 4px;
  font-size: 11px;
}
.scope-row-perms {
  font-family: "Cascadia Code", "Consolas", monospace;
  font-size: 10px;
  color: #8a8886;
  line-height: 1.4;
  word-break: break-word;
  margin: 0;
}
.scope-row-connect {
  align-self: flex-start;
  margin-top: 2px;
  padding: 4px 12px;
  border-radius: 4px;
  border: 1px solid #0078d4;
  background: #0078d4;
  color: #fff;
  font-size: 12px;
  font-weight: 600;
  cursor: pointer;
  transition: background 0.15s;
}
.scope-row-connect:hover {
  background: #106ebe;
  border-color: #106ebe;
}
.scope-row-status {
  font-size: 11.5px;
  font-weight: 600;
  color: #107c10;
}
.scope-row-add {
  align-self: flex-start;
  margin-top: 4px;
  padding: 4px 12px;
  border-radius: 4px;
  border: 1px solid #0078d4;
  background: #0078d4;
  color: #fff;
  font-size: 12px;
  font-weight: 600;
  cursor: pointer;
  transition:
    background 0.15s,
    border-color 0.15s;
}
.scope-row-add:hover {
  background: #106ebe;
  border-color: #106ebe;
}
.addons-divider {
  height: 1px;
  background: #edebe9;
  margin: 4px 0 2px;
}
.scope-grant-all {
  display: flex;
  align-items: flex-start;
  gap: 8px;
  padding: 8px 9px;
  border-radius: 6px;
  border: 1px dashed #b3b0ad;
  background: #fafafa;
  text-align: left;
  cursor: pointer;
  transition: all 0.15s;
  width: 100%;
}
.scope-grant-all:hover {
  border-color: #605e5c;
  border-style: solid;
  background: #f3f2f1;
}
.scope-grant-all-icon {
  flex-shrink: 0;
  font-size: 14px;
  margin-top: 1px;
}
.scope-grant-all-body {
  display: flex;
  flex-direction: column;
  gap: 2px;
  min-width: 0;
  flex: 1;
}
.scope-grant-all-title {
  font-size: 12.5px;
  font-weight: 600;
  color: #323130;
  line-height: 1.3;
  display: inline-flex;
  align-items: center;
  gap: 6px;
  flex-wrap: wrap;
}
.scope-grant-all-tag {
  font-size: 9.5px;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.4px;
  padding: 1px 5px;
  border-radius: 3px;
  background: #fff4ce;
  color: #8a6914;
}
.scope-grant-all-desc {
  font-size: 11px;
  color: #605e5c;
  line-height: 1.35;
}
.azure-revoke-btn {
  width: 100%;
  padding: 6px 8px;
  margin-top: 4px;
  border-radius: 4px;
  border: none;
  background: transparent;
  color: #8a8886;
  font-size: 11.5px;
  cursor: pointer;
  transition: color 0.15s;
  text-decoration: underline;
  text-underline-offset: 2px;
}
.azure-revoke-btn:hover {
  color: #d13438;
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

/* ── Empty state ── */
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
  font-size: 1.5rem;
  font-weight: 600;
  margin: 0 0 0.3rem;
  color: #323130;
}
.es-eyebrow {
  font-size: 11px;
  font-weight: 600;
  letter-spacing: 0.06em;
  text-transform: uppercase;
  color: #0078d4;
  margin: 0 0 0.6rem;
  text-align: center;
}
.es-sub {
  font-size: 13px;
  color: #605e5c;
  margin: 0 0 1rem;
  line-height: 1.5;
  text-align: center;
  max-width: 480px;
}
.es-connect-bar {
  margin-bottom: 1rem;
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 8px;
}
.es-connect-bar .es-step-btn {
  display: inline-flex;
  align-items: center;
  gap: 8px;
  padding: 8px 20px;
  font-size: 13px;
  border-radius: 4px;
  border: none;
  font-weight: 600;
  cursor: pointer;
}
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
  font-size: 13px;
  font-weight: 600;
  color: #323130;
  line-height: 1.3;
  margin-bottom: 3px;
}
.es-cap-desc {
  font-size: 12px;
  color: #605e5c;
  line-height: 1.45;
}

/* ── Quick grid cards ── */
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
  border: 1px solid #e1dfdd;
  border-radius: 4px;
  background: #fff;
  cursor: pointer;
  font: inherit;
  font-size: 13px;
  color: #323130;
  text-align: left;
  opacity: 0;
  animation: staggerSlideIn 0.5s ease forwards;
  transition:
    border-color 0.15s,
    background 0.15s;
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
  background: #f3f2f1;
}
.es-quick-icon {
  width: 28px;
  height: 28px;
  border-radius: 4px;
  background: #deecf9;
  color: #0078d4;
  display: flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
}
.es-quick-icon--teal {
  background: #d2f0e8;
  color: #008575;
}
.es-quick-icon--orange {
  background: #fed9cc;
  color: #d83b01;
}
.es-quick-icon--purple {
  background: #e8daef;
  color: #8661c5;
}
.es-quick-icon--green {
  background: #dff6dd;
  color: #107c10;
}
.es-quick-icon--pink {
  background: #f9e0f0;
  color: #b4009e;
}
.es-quick-icon--blue {
  background: #deecf9;
  color: #0078d4;
}
.es-quick-icon--indigo {
  background: #e0e4f5;
  color: #4f6bed;
}
.es-quick-icon--navy {
  background: #d6dbe4;
  color: #002050;
}
.es-quick-label {
  font-weight: 600;
  line-height: 1.3;
}

/* ── Onboarding / Steps ── */
.es-onboarding {
  width: 100%;
  max-width: 760px;
  margin: 0 0 1.25rem;
  padding: 1rem;
  border: 1px solid #e1dfdd;
  border-radius: 8px;
  background: #fff;
  box-shadow: 0 2px 8px rgba(0, 0, 0, 0.06);
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
  border: 1px solid #e1dfdd;
  border-radius: 8px;
  background: #fff;
}
.es-step--active {
  border-color: #0078d4;
}
.es-step--done {
  border-color: #107c10;
  background: #f1faf1;
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
  background: #deecf9;
  color: #0078d4;
  font-size: 13px;
  font-weight: 700;
}
.es-step--done .es-step-badge {
  background: #dff6dd;
  color: #107c10;
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
  font-size: 14px;
  font-weight: 600;
  color: #323130;
}
.es-step-state {
  padding: 2px 8px;
  border-radius: 4px;
  background: #dff6dd;
  color: #107c10;
  font-size: 11px;
  font-weight: 600;
}
.es-step-state--pending {
  background: #deecf9;
  color: #0078d4;
}
.es-step-state--blocked {
  background: #f3f2f1;
  color: #605e5c;
}
.es-step-copy {
  margin: 0 0 0.75rem;
  color: #605e5c;
  font-size: 13px;
  line-height: 1.5;
}
.es-step-btn {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  min-height: 34px;
  padding: 6px 16px;
  border: 1px solid transparent;
  border-radius: 4px;
  font: inherit;
  font-size: 13px;
  font-weight: 600;
  cursor: pointer;
  transition:
    background 0.15s,
    color 0.15s;
}
.es-step-btn:disabled {
  opacity: 0.7;
  cursor: wait;
}
.es-step-btn--github {
  background: #323130;
  color: #fff;
}
.es-step-btn--github:hover {
  background: #484644;
}
.es-step-btn--azure {
  background: #0078d4;
  color: #fff;
  width: 100%;
  max-width: 360px;
}
.es-step-btn--azure:hover {
  background: #106ebe;
}

/* ── Feature cards ── */
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
  border: 1px solid #e1dfdd;
  border-radius: 8px;
  background: #fff;
  transition: border-color 0.15s;
}
.es-feature:hover {
  border-color: #0078d4;
}
.es-feature-icon {
  flex-shrink: 0;
  width: 34px;
  height: 34px;
  border-radius: 4px;
  background: #0078d4;
  color: #fff;
  display: flex;
  align-items: center;
  justify-content: center;
}
.es-feature-icon--safe {
  background: #107c10;
}
.es-feature-text {
  display: flex;
  flex-direction: column;
  gap: 2px;
  min-width: 0;
}
.es-feature-text strong {
  font-size: 13px;
  font-weight: 600;
  color: #323130;
}
.es-feature-text span {
  font-size: 12px;
  color: #605e5c;
  line-height: 1.4;
}

/* ── API pills ── */
.es-api-pills {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
  justify-content: center;
  margin-bottom: 1rem;
}
.es-api-pill {
  padding: 3px 10px;
  border-radius: 4px;
  border: 1px solid #e1dfdd;
  font-size: 12px;
  color: #605e5c;
  background: #fff;
}

/* ── Comparison table ── */
.es-compare {
  width: 100%;
  margin-bottom: 1rem;
  border: 1px solid #e1dfdd;
  border-radius: 8px;
  overflow: hidden;
}
.es-compare-table {
  width: 100%;
  border-collapse: collapse;
  font-size: 13px;
  line-height: 1.4;
}
.es-compare-table th {
  background: #f3f2f1;
  font-weight: 600;
  text-align: left;
  padding: 8px 10px;
  color: #323130;
  border-bottom: 1px solid #e1dfdd;
  font-size: 12px;
  text-transform: uppercase;
  letter-spacing: 0.03em;
}
.es-compare-table th.es-compare-us {
  color: #0078d4;
}
.es-compare-table td {
  padding: 6px 10px;
  border-bottom: 1px solid #f3f2f1;
  color: #605e5c;
}
.es-compare-table td:first-child {
  font-weight: 600;
  color: #323130;
  min-width: 120px;
}
.es-compare-table td.es-compare-us {
  color: #323130;
}
.es-compare-table tr:last-child td {
  border-bottom: none;
}
.es-compare-table tr:hover td {
  background: #f3f2f1;
}

/* ── Prompt pills ── */
.es-prompts {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
  justify-content: center;
  margin-bottom: 1rem;
}
.es-prompt {
  padding: 6px 14px;
  border: 1px solid #e1dfdd;
  border-radius: 4px;
  background: transparent;
  color: #323130;
  font-size: 13px;
  cursor: pointer;
  transition:
    border-color 0.15s,
    color 0.15s;
}
.es-prompt:hover {
  border-color: #0078d4;
  color: #0078d4;
}

/* ── Messages ── */
.message-row {
  display: flex;
  width: 100%;
  min-width: 0;
  max-width: 100%;
  animation: messageSlideIn 0.3s ease;
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
  border-radius: 8px;
  padding: 10px 14px;
  background: #f3f2f1;
  color: #323130;
  font-size: 14px;
  line-height: 1.5;
  word-wrap: break-word;
}
.ai-row {
  display: flex;
  flex-direction: column;
  gap: 6px;
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
  gap: 8px;
  min-height: 28px;
}
.ai-content {
  min-width: 0;
  max-width: 100%;
  overflow-x: auto;
}
.message-text {
  font-size: 14px;
  line-height: 1.6;
  word-wrap: break-word;
  color: #323130;
}
.streaming-cursor {
  display: inline-block;
  width: 6px;
  height: 16px;
  background: #605e5c;
  border-radius: 1px;
  margin-left: 2px;
  vertical-align: text-bottom;
  animation: cursor-pulse 1s ease-in-out infinite;
}
.stream-intent {
  font-style: italic;
  color: #605e5c;
  font-size: 14px;
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

/* ── Message content styling ── */
.message-text :deep(pre) {
  background: #f3f2f1;
  border: 1px solid #e1dfdd;
  border-radius: 4px;
  padding: 12px 16px;
  margin: 8px 0;
  overflow-x: auto;
  font-size: 13px;
}
.message-text :deep(code) {
  background: rgba(0, 0, 0, 0.06);
  padding: 2px 6px;
  border-radius: 3px;
  font-size: 0.9em;
  font-family: "Cascadia Code", "Fira Code", Consolas, monospace;
}
.message-text :deep(pre code) {
  background: none;
  padding: 0;
}
.message-text :deep(table) {
  border-collapse: collapse;
  width: 100%;
  margin: 8px 0;
  font-size: 13px;
  display: block;
  overflow-x: auto;
}
.message-text :deep(th),
.message-text :deep(td) {
  border: 1px solid #e1dfdd;
  padding: 6px 10px;
  text-align: left;
}
.message-text :deep(th) {
  background: #f3f2f1;
  font-weight: 600;
  font-size: 12px;
}
.message-text :deep(tr:nth-child(even)) {
  background: #faf9f8;
}
.message-text :deep(h2),
.message-text :deep(h3),
.message-text :deep(h4) {
  margin: 12px 0 4px;
  font-weight: 600;
  color: #323130;
}
.message-text :deep(h2) {
  font-size: 18px;
}
.message-text :deep(h3) {
  font-size: 16px;
}
.message-text :deep(h4) {
  font-size: 14px;
}
.message-text :deep(ul) {
  margin: 4px 0;
  padding-left: 1.5rem;
}
.message-text :deep(li) {
  margin: 2px 0;
}

/* ── Charts ── */
.chart-container {
  width: 100%;
  height: 340px;
  margin: 0 0 12px;
  border: 1px solid #e1dfdd;
  border-radius: 8px;
  background: #fff;
  overflow: hidden;
}

/* ── Input area ── */
.input-area {
  flex-shrink: 0;
  padding: 12px 16px;
  max-width: 900px;
  margin: 0 auto;
  width: 100%;
  padding-bottom: max(12px, env(safe-area-inset-bottom));
  display: flex;
  align-items: center;
  gap: 6px;
}
.input-wrapper {
  display: flex;
  flex-direction: column;
  border: 1px solid #8a8886;
  border-radius: 20px;
  padding: 14px 18px 10px 18px;
  background: #fff;
  box-shadow: 0 2px 8px rgba(0, 0, 0, 0.08);
  transition:
    border-color 0.15s,
    box-shadow 0.15s;
  flex: 1;
  min-width: 0;
}
.input-wrapper:focus-within {
  border-color: #0078d4;
  box-shadow: 0 0 0 2px rgba(0, 120, 212, 0.1);
}
.input-wrapper--disabled {
  background: #f3f2f1;
  opacity: 0.6;
}
.input-field {
  flex: 1;
  background: transparent;
  border: none;
  color: #323130;
  font-size: 15px;
  font-family: inherit;
  padding: 0;
  outline: none;
  line-height: 1.5;
  resize: none;
  overflow-y: hidden;
  max-height: 400px;
  min-height: 24px;
}
.input-field::placeholder {
  color: #a19f9d;
}
.input-bottom-bar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-top: 8px;
}
.input-bottom-left {
  display: flex;
  align-items: center;
  gap: 4px;
}
.input-action-btn {
  display: flex;
  align-items: center;
  gap: 5px;
  height: 28px;
  padding: 0 10px;
  border-radius: 14px;
  border: none;
  background: transparent;
  color: #605e5c;
  cursor: pointer;
  font-family: inherit;
  font-size: 12px;
  font-weight: 500;
  white-space: nowrap;
  transition:
    color 0.15s,
    background 0.15s;
}
.input-action-btn:hover:not(:disabled) {
  background: #f3f2f1;
  color: #0078d4;
}
.input-action-btn:disabled {
  opacity: 0.35;
  cursor: default;
}
.input-bottom-right {
  display: flex;
  align-items: center;
}
.action-btn {
  flex-shrink: 0;
  width: 32px;
  height: 32px;
  display: flex;
  align-items: center;
  justify-content: center;
  border-radius: 50%;
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
  background: #f3f2f1;
  color: #a19f9d;
  cursor: default;
}
.action-btn--stop {
  background: #323130;
  color: #fff;
}
.action-btn--stop:hover {
  background: #484644;
}

/* ── Tools sidebar (right) ── */
.tools-sidebar {
  width: 0;
  flex-shrink: 0;
  border-left: 1px solid #e1dfdd;
  background: #fff;
  overflow: hidden;
  transition: width 0.25s ease;
  display: flex;
  flex-direction: column;
  font-size: 13px;
}
.tools-sidebar--open {
  width: 220px;
}
.tools-sidebar-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 8px 16px;
  border-bottom: 1px solid #e1dfdd;
}
.tools-sidebar-title {
  font-size: 11px;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.04em;
  color: #605e5c;
}
.tools-sidebar-scroll {
  flex: 1;
  overflow-y: auto;
  padding: 4px 8px;
  scrollbar-width: thin;
}
.st-count {
  font-size: 11px;
  font-weight: 600;
  background: #f3f2f1;
  color: #605e5c;
  border-radius: 4px;
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
  padding: 5px 16px;
  border-radius: 0;
  cursor: default;
  user-select: none;
  transition: background 0.1s;
  min-height: 28px;
}
.st-row:hover {
  background: #f3f2f1;
}
.st-row--clickable {
  cursor: pointer;
}
.st-row--running {
  background: #fff4ce;
}
.st-name {
  flex: 1;
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  color: #323130;
  font-weight: 400;
  font-size: 13px;
}
.st-time {
  flex-shrink: 0;
  color: #605e5c;
  font-size: 11px;
}

/* ── Tool popover ── */
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
  border: 1px solid #e1dfdd;
  border-radius: 8px;
  box-shadow: 0 8px 30px rgba(0, 0, 0, 0.12);
  overflow: hidden;
  display: flex;
  flex-direction: column;
  animation: popover-in 0.15s ease-out;
  font-family: "Cascadia Code", "Fira Code", Consolas, monospace;
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
  padding: 14px 20px;
  border-bottom: 1px solid #e1dfdd;
  background: #f3f2f1;
}
.tool-popover-name {
  font-size: 16px;
  font-weight: 600;
  color: #323130;
  flex: 1;
}
.tool-popover-time {
  font-size: 14px;
  color: #605e5c;
}
.tool-popover-close {
  background: none;
  border: none;
  font-size: 20px;
  color: #605e5c;
  cursor: pointer;
  padding: 0 4px;
  line-height: 1;
}
.tool-popover-close:hover {
  color: #323130;
}
.tool-popover-section {
  padding: 12px 20px;
  border-bottom: 1px solid #f3f2f1;
}
.tool-popover-section:last-child {
  border-bottom: none;
}
.tool-popover-label {
  font-size: 12px;
  font-weight: 700;
  letter-spacing: 0.06em;
  color: #605e5c;
  margin-bottom: 6px;
  text-transform: uppercase;
}
.tool-popover-pre {
  background: #f3f2f1;
  border: 1px solid #e1dfdd;
  border-radius: 4px;
  padding: 12px 16px;
  margin: 0;
  font-size: 13px;
  white-space: pre-wrap;
  word-break: break-word;
  max-height: 350px;
  overflow-y: auto;
  line-height: 1.5;
  color: #323130;
  scrollbar-width: thin;
}
.tool-popover-pre--error {
  color: #d13438;
  background: #fde7e9;
  border-color: #d13438;
}

/* ── Auth buttons ── */
.login-btn {
  display: flex;
  align-items: center;
  gap: 8px;
  width: 100%;
  padding: 9px 14px;
  border-radius: 4px;
  font-size: 13px;
  font-weight: 500;
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
  background: #484644;
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

/* ── Auth overlay ── */
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
  padding: 40px 48px;
  border-radius: 8px;
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
  color: #323130;
  margin: 0;
}
.auth-overlay-sub {
  font-size: 13px;
  color: #605e5c;
  margin: 0;
}

/* ── Misc sidebar elements ── */
.sidebar-linkedin {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 7px;
  padding: 7px 10px;
  border-radius: 4px;
  border: 1px solid #0078d4;
  color: #0078d4;
  font-size: 13px;
  font-weight: 600;
  text-decoration: none;
  transition: background 0.15s;
}
.sidebar-linkedin:hover {
  background: #f3f2f1;
}
.new-chat-btn {
  display: flex;
  align-items: center;
  gap: 6px;
  width: 100%;
  padding: 7px 12px;
  border-radius: 4px;
  border: 1px solid #e1dfdd;
  background: #fff;
  color: #323130;
  font-size: 13px;
  font-weight: 500;
  cursor: pointer;
  transition: all 0.15s;
}
.new-chat-btn:hover {
  background: #f3f2f1;
  border-color: #0078d4;
  color: #0078d4;
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
  font-size: 13px;
  font-weight: 500;
  color: #323130;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
.sidebar-user-login {
  font-size: 11px;
  color: #605e5c;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
.sidebar-logout-btn {
  flex-shrink: 0;
  background: transparent;
  border: none;
  color: #605e5c;
  cursor: pointer;
  padding: 4px;
  border-radius: 4px;
  display: flex;
  align-items: center;
  justify-content: center;
  transition: all 0.15s;
}
.sidebar-logout-btn:hover {
  color: #d13438;
  background: #fde7e9;
}
.model-selector {
  padding: 4px 0 8px;
  border-bottom: 1px solid #e1dfdd;
  margin-bottom: 4px;
}
.model-selector-label {
  display: block;
  font-size: 11px;
  font-weight: 600;
  color: #605e5c;
  text-transform: uppercase;
  letter-spacing: 0.04em;
  margin-bottom: 4px;
}
.model-selector-select {
  width: 100%;
  padding: 5px 8px;
  font-size: 13px;
  border: 1px solid #e1dfdd;
  border-radius: 4px;
  background: #fff;
  color: #323130;
  cursor: pointer;
  outline: none;
}

/* ── Build badge ── */
.build-badge {
  position: fixed;
  bottom: 12px;
  right: 16px;
  font-size: 14px;
  font-family: "Cascadia Code", "Fira Code", Consolas, monospace;
  color: #605e5c;
  background: #f3f2f1;
  padding: 2px 8px;
  border-radius: 4px;
  pointer-events: none;
  z-index: 1000;
  font-weight: 600;
}

/* ── PPTX download ── */
.pptx-suggest {
  display: flex;
  justify-content: center;
  padding: 0 1rem 4px;
  max-width: 800px;
  margin: 0 auto;
  width: 100%;
}
.pptx-suggest-btn {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  padding: 7px 16px;
  border-radius: 4px;
  border: 1px solid #e1dfdd;
  background: #fff;
  color: #323130;
  font-size: 13px;
  font-family: inherit;
  cursor: pointer;
  transition: all 0.15s;
}
.pptx-suggest-btn:hover {
  border-color: #0078d4;
  color: #0078d4;
}
.pptx-download-bar {
  display: flex;
  justify-content: center;
  padding: 0 1rem 8px;
  max-width: 800px;
  margin: 0 auto;
  width: 100%;
}
.pptx-download-btn {
  display: inline-flex;
  align-items: center;
  gap: 8px;
  padding: 8px 18px;
  border-radius: 4px;
  background: #0078d4;
  color: #fff;
  font-size: 14px;
  font-weight: 500;
  text-decoration: none;
  cursor: pointer;
  transition: background 0.15s;
  border: none;
}
.pptx-download-btn:hover {
  background: #106ebe;
}
.pptx-inline-download {
  margin-top: 8px;
}

/* ── Script inline block ── */
.script-download-bar {
  display: flex;
  justify-content: center;
  padding: 0 1rem 8px;
  max-width: 800px;
  margin: 0 auto;
  width: 100%;
}
.script-inline-block {
  margin-top: 12px;
  border: 1px solid #e1dfdd;
  border-radius: 6px;
  overflow: hidden;
  background: #fafafa;
  width: 100%;
}
.script-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 8px 12px;
  background: #f3f2f1;
  border-bottom: 1px solid #e1dfdd;
  gap: 8px;
}
.script-header-left {
  display: flex;
  align-items: center;
  gap: 8px;
  min-width: 0;
}
.script-filename {
  font-size: 13px;
  font-weight: 600;
  color: #323130;
  white-space: nowrap;
}
.script-meta {
  font-size: 12px;
  color: #605e5c;
  white-space: nowrap;
}
.script-header-actions {
  display: flex;
  align-items: center;
  gap: 6px;
  flex-shrink: 0;
}
.script-copy-btn {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 28px;
  height: 28px;
  border-radius: 4px;
  background: transparent;
  border: 1px solid #e1dfdd;
  color: #605e5c;
  cursor: pointer;
  transition:
    background 0.15s,
    color 0.15s;
}
.script-copy-btn:hover {
  background: #deecf9;
  color: #0078d4;
}
.script-download-btn {
  display: inline-flex;
  align-items: center;
  gap: 5px;
  padding: 4px 12px;
  border-radius: 4px;
  background: #0078d4;
  color: #fff;
  font-size: 12px;
  font-weight: 500;
  text-decoration: none;
  cursor: pointer;
  transition: background 0.15s;
  border: none;
  white-space: nowrap;
}
.script-download-btn:hover {
  background: #106ebe;
}
.script-description {
  padding: 6px 12px;
  font-size: 12px;
  color: #605e5c;
  background: #f9f9f9;
  border-bottom: 1px solid #e1dfdd;
}
.script-code {
  margin: 0;
  padding: 12px 14px;
  background: #1e1e1e;
  color: #d4d4d4;
  font-size: 12px;
  font-family: "Cascadia Code", "Fira Code", "Consolas", monospace;
  line-height: 1.5;
  overflow-x: auto;
  max-height: 350px;
  overflow-y: auto;
  white-space: pre;
}
.script-code code {
  font-family: inherit;
  font-size: inherit;
}

/* ── Follow-up buttons ── */
.follow-up-buttons {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  margin-top: 12px;
}
.follow-up-btn {
  background: #deecf9;
  color: #0078d4;
  border: 1px solid rgba(0, 120, 212, 0.3);
  border-radius: 4px;
  padding: 6px 14px;
  font-size: 13px;
  cursor: pointer;
  transition: background 0.15s;
  line-height: 1.4;
  text-align: left;
}
.follow-up-btn:hover {
  background: #c7e0f4;
  border-color: #0078d4;
}

/* ── Mobile ── */
.mobile-auth-bar {
  display: none;
  align-items: center;
  gap: 8px;
  padding: 0 12px 4px;
  flex-wrap: wrap;
  justify-content: center;
}
.mobile-auth-btn {
  display: inline-flex;
  align-items: center;
  gap: 8px;
  padding: 10px 22px;
  border-radius: 4px;
  font-size: 14px;
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
  background: #484644;
}
.mobile-auth-btn--azure {
  background: #fff;
  color: #323130;
  border: 1px solid #e1dfdd;
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
  border-radius: 4px;
  background: #f3f2f1;
  border: 1px solid #e1dfdd;
  font-size: 12px;
  color: #323130;
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
  border-radius: 4px;
  background: #fff;
  border: 1px solid #e1dfdd;
}
.mobile-user-avatar {
  width: 20px;
  height: 20px;
  border-radius: 50%;
}
.mobile-user-name {
  font-size: 12px;
  font-weight: 500;
  color: #323130;
  max-width: 80px;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
.es-tagline-mobile {
  display: none;
  font-size: 13px;
  color: #605e5c;
  margin: 8px 16px 0;
  padding: 0 16px;
  text-align: center;
  line-height: 1.5;
}

/* ── Responsive ── */
@media (max-width: 768px) {
  .sidebar {
    position: fixed;
    top: 48px;
    left: 0;
    bottom: 0;
    width: 80vw;
    max-width: 320px;
    z-index: 150;
    background: #fff;
    box-shadow: 2px 0 12px rgba(0, 0, 0, 0.18);
    transform: translateX(0);
    transition: transform 0.2s ease;
  }
  .sidebar--collapsed {
    transform: translateX(-100%);
    box-shadow: none;
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
    font-size: 12px;
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
    padding: 1rem;
    justify-content: center;
    flex: 1;
  }
  .messages-inner {
    padding: 12px;
    flex: 1;
    display: flex;
    flex-direction: column;
  }
  .input-area {
    padding: 8px 12px;
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
@media (max-width: 600px) {
  .es-features {
    grid-template-columns: 1fr;
  }
  .es-headline {
    font-size: 1.3rem;
  }
}
@media (max-width: 768px) {
  .mobile-auth-bar {
    display: flex !important;
  }
  .es-tagline-mobile {
    display: block !important;
  }
}
</style>
