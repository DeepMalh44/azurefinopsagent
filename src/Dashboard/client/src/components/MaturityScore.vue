<template>
  <div class="maturity-panel" v-if="visible">
    <div class="maturity-header">
      <span class="maturity-title">FinOps Maturity</span>
      <button
        class="maturity-refresh"
        @click="refresh"
        :disabled="loading"
        title="Refresh scores"
      >
        <svg
          :class="{ spin: loading }"
          width="14"
          height="14"
          viewBox="0 0 24 24"
          fill="none"
          stroke="currentColor"
          stroke-width="2"
          stroke-linecap="round"
          stroke-linejoin="round"
        >
          <polyline points="23 4 23 10 17 10" />
          <polyline points="1 20 1 14 7 14" />
          <path
            d="M3.51 9a9 9 0 0114.85-3.36L23 10M1 14l4.64 4.36A9 9 0 0020.49 15"
          />
        </svg>
      </button>
    </div>

    <div class="maturity-levels">
      <div
        v-for="level in levels"
        :key="level.name"
        class="maturity-level"
        :class="'maturity-level--' + level.name.toLowerCase()"
      >
        <div class="level-header" @click="level.expanded = !level.expanded">
          <div class="level-name-row">
            <span
              class="level-badge"
              :style="{ background: scoreColor(level.overall) }"
              >{{ level.overall }}</span
            >
            <span class="level-name">{{ level.name }}</span>
          </div>
          <svg
            class="level-chevron"
            :class="{ 'level-chevron--open': level.expanded }"
            viewBox="0 0 16 16"
            fill="none"
            width="12"
            height="12"
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
        <div class="level-bar-track">
          <div
            class="level-bar-fill"
            :style="{
              width: level.overall + '%',
              background: scoreColor(level.overall),
            }"
          ></div>
        </div>
        <div v-if="level.expanded" class="level-details">
          <div
            v-for="cat in level.categories"
            :key="cat.id"
            class="cat-row"
            @click="$emit('prompt', promptForCategory(cat.id))"
          >
            <span
              class="cat-dot"
              :style="{ background: scoreColor(cat.score) }"
            ></span>
            <span class="cat-label">{{ cat.label }}</span>
            <span class="cat-score" :style="{ color: scoreColor(cat.score) }">{{
              cat.score
            }}</span>
          </div>
          <div
            v-if="level.categories.length === 0"
            class="cat-row cat-row--empty"
          >
            <span class="cat-label cat-label--locked">Connect to unlock</span>
          </div>
        </div>
      </div>
    </div>

    <div v-if="hints.length" class="maturity-hints">
      <div v-for="hint in hints" :key="hint" class="hint-row">
        <span class="hint-icon">🔒</span>
        <span class="hint-text">{{ hint }}</span>
      </div>
    </div>
  </div>
</template>

<script setup>
import { defineEmits, defineProps, ref, watch } from "vue";

const props = defineProps({
  azureConnected: Boolean,
  graphEnabled: Boolean,
  logAnalyticsEnabled: Boolean,
});

const emit = defineEmits(["prompt"]);

const loading = ref(false);
const visible = ref(false);
const hints = ref([]);

const levels = ref([
  { name: "Crawl", overall: 0, categories: [], expanded: false },
  { name: "Walk", overall: 0, categories: [], expanded: false },
  { name: "Run", overall: 0, categories: [], expanded: false },
]);

function scoreColor(score) {
  if (score >= 75) return "#107c10";
  if (score >= 50) return "#ff8c00";
  if (score >= 25) return "#d83b01";
  return "#a80000";
}

const categoryPrompts = {
  tagging:
    "Audit my resource tagging coverage. What percentage of resources have cost-center, environment, and department tags? Show the untagged resources by resource group.",
  orphaned:
    "Find all orphaned resources — unattached disks, unused public IPs, NICs not attached to VMs. List them with monthly cost so I can clean them up.",
  advisor:
    "Show all open Azure Advisor cost recommendations grouped by impact. What are the estimated annual savings for each?",
  budgets:
    "Show my Azure budgets vs actual spend. Which subscriptions are missing budgets? Help me set up budget alerts.",
  reservations:
    "Analyze my reservation and savings plan coverage. What percentage of eligible compute is covered? What new reservations does Azure recommend?",
  autoshutdown:
    "Which of my VMs have auto-shutdown configured? Identify dev/test VMs without auto-shutdown and estimate the savings from enabling it.",
  rightsizing:
    "Show right-sizing recommendations for my VMs. For each, show current vs recommended SKU and the monthly savings.",
  taggingpolicy:
    "Check my Azure Policy assignments related to tagging. Do I have deny or audit policies for required tags? Show compliance status.",
  exports:
    "Do I have cost exports configured? Show the status of Cost Management exports across my subscriptions.",
  mgmtgroups:
    "Show my management group hierarchy and how subscriptions are organized. Is my structure suitable for cost governance?",
  licenses:
    "Analyze my M365 license usage — purchased vs assigned vs unused. Show waste from unassigned licenses.",
};

function promptForCategory(catId) {
  return (
    categoryPrompts[catId] || "Show my FinOps maturity assessment details."
  );
}

async function refresh() {
  loading.value = true;
  try {
    const r = await fetch("/api/assessment");
    if (r.ok) {
      const data = await r.json();
      visible.value = true;

      levels.value[0].overall = data.crawl?.overall ?? 0;
      levels.value[0].categories = data.crawl?.categories ?? [];
      levels.value[1].overall = data.walk?.overall ?? 0;
      levels.value[1].categories = data.walk?.categories ?? [];
      levels.value[2].overall = data.run?.overall ?? 0;
      levels.value[2].categories = data.run?.categories ?? [];
      hints.value = data.unlockHints ?? [];

      // Auto-expand crawl on first load
      if (
        !levels.value[0].expanded &&
        !levels.value[1].expanded &&
        !levels.value[2].expanded
      ) {
        levels.value[0].expanded = true;
      }
    }
  } catch {
    // silently fail — panel just stays empty
  } finally {
    loading.value = false;
  }
}

// Auto-run when Azure connects or consent tiers change
watch(
  () => props.azureConnected,
  (connected) => {
    if (connected) refresh();
    else visible.value = false;
  },
  { immediate: true },
);

watch(
  () => props.graphEnabled,
  (enabled) => {
    if (enabled) refresh();
  },
);
watch(
  () => props.logAnalyticsEnabled,
  (enabled) => {
    if (enabled) refresh();
  },
);
</script>

<style scoped>
.maturity-panel {
  border-top: 1px solid #e1dfdd;
  padding: 10px 12px 8px;
}
.maturity-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 8px;
}
.maturity-title {
  font-size: 11px;
  font-weight: 600;
  color: #605e5c;
  text-transform: uppercase;
  letter-spacing: 0.5px;
}
.maturity-refresh {
  background: none;
  border: none;
  color: #605e5c;
  cursor: pointer;
  padding: 2px;
  border-radius: 4px;
  display: flex;
  align-items: center;
}
.maturity-refresh:hover {
  color: #0078d4;
  background: #f3f2f1;
}
.maturity-refresh:disabled {
  opacity: 0.4;
  cursor: default;
}
.spin {
  animation: spin 0.8s linear infinite;
}
@keyframes spin {
  to {
    transform: rotate(360deg);
  }
}

.maturity-levels {
  display: flex;
  flex-direction: column;
  gap: 6px;
}
.maturity-level {
  background: #faf9f8;
  border: 1px solid #e1dfdd;
  border-radius: 6px;
  overflow: hidden;
}
.level-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 6px 8px;
  cursor: pointer;
  user-select: none;
}
.level-header:hover {
  background: #f3f2f1;
}
.level-name-row {
  display: flex;
  align-items: center;
  gap: 8px;
}
.level-badge {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 28px;
  height: 20px;
  border-radius: 10px;
  font-size: 11px;
  font-weight: 700;
  color: #fff;
}
.level-name {
  font-size: 13px;
  font-weight: 600;
  color: #323130;
}
.level-chevron {
  transition: transform 0.15s;
}
.level-chevron--open {
  transform: rotate(180deg);
}
.level-bar-track {
  height: 3px;
  background: #e1dfdd;
}
.level-bar-fill {
  height: 100%;
  border-radius: 0 2px 2px 0;
  transition: width 0.5s ease;
}

.level-details {
  padding: 4px 8px 6px;
  border-top: 1px solid #e1dfdd;
}
.cat-row {
  display: flex;
  align-items: center;
  gap: 6px;
  padding: 3px 4px;
  border-radius: 4px;
  cursor: pointer;
  font-size: 12px;
}
.cat-row:hover {
  background: #edebe9;
}
.cat-row--empty {
  cursor: default;
}
.cat-row--empty:hover {
  background: transparent;
}
.cat-dot {
  width: 8px;
  height: 8px;
  border-radius: 50%;
  flex-shrink: 0;
}
.cat-label {
  flex: 1;
  color: #323130;
}
.cat-label--locked {
  color: #a19f9d;
  font-style: italic;
}
.cat-score {
  font-weight: 600;
  font-size: 11px;
  min-width: 20px;
  text-align: right;
}

.maturity-hints {
  margin-top: 6px;
  padding-top: 6px;
  border-top: 1px solid #e1dfdd;
}
.hint-row {
  display: flex;
  align-items: center;
  gap: 6px;
  padding: 2px 0;
}
.hint-icon {
  font-size: 10px;
}
.hint-text {
  font-size: 11px;
  color: #605e5c;
}
</style>
