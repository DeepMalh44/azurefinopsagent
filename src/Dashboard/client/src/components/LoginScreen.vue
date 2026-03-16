<template>
  <div class="login-screen">
    <div class="login-content">
      <svg
        class="hub-svg"
        viewBox="25 15 350 290"
        xmlns="http://www.w3.org/2000/svg"
      >
        <!-- Connection lines (dashed) -->
        <line
          v-for="(n, i) in nodes"
          :key="'l' + i"
          x1="200"
          y1="165"
          :x2="n.x"
          :y2="n.y"
          stroke="#d1d9e0"
          stroke-width="1"
          stroke-dasharray="4 3"
          opacity="0.7"
        />

        <!-- Animated dots traveling to center -->
        <circle
          v-for="(n, i) in nodes"
          :key="'d' + i"
          r="3"
          :fill="n.color"
          opacity="0.8"
        >
          <animateMotion
            dur="2.5s"
            repeatCount="indefinite"
            :path="`M${n.x},${n.y} L200,165`"
            :begin="i * 0.28 + 's'"
          />
        </circle>

        <!-- Center - outer pulse ring -->
        <circle
          cx="200"
          cy="165"
          r="46"
          fill="none"
          stroke="#1f2328"
          stroke-width="1"
          opacity="0.12"
        >
          <animate
            attributeName="r"
            values="46;58;46"
            dur="3s"
            repeatCount="indefinite"
          />
          <animate
            attributeName="opacity"
            values="0.12;0;0.12"
            dur="3s"
            repeatCount="indefinite"
          />
        </circle>

        <!-- Center - glow -->
        <circle cx="200" cy="165" r="42" fill="#1f2328" opacity="0.04">
          <animate
            attributeName="r"
            values="40;46;40"
            dur="3s"
            repeatCount="indefinite"
          />
        </circle>

        <!-- Center - main circle -->
        <circle
          cx="200"
          cy="165"
          r="38"
          fill="#1f2328"
          stroke="#1f2328"
          stroke-width="2"
        />

        <!-- Center - AI text -->
        <text
          x="200"
          y="172"
          text-anchor="middle"
          fill="white"
          font-size="28"
          font-weight="700"
          letter-spacing="2"
          font-family="-apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif"
        >
          AI
        </text>

        <!-- Source node pills (rendered on top of lines) -->
        <g v-for="(n, i) in nodes" :key="'n' + i">
          <rect
            :x="n.x - n.w / 2"
            :y="n.y - 14"
            :width="n.w"
            height="28"
            rx="14"
            :fill="n.color"
          />
          <text
            :x="n.x"
            :y="n.y + 5"
            text-anchor="middle"
            fill="white"
            font-size="12"
            font-weight="600"
            font-family="-apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif"
          >
            {{ n.label }}
          </text>
        </g>
      </svg>

      <p class="tagline">Azure FinOps Agent</p>

      <a href="/auth/github" class="github-btn">
        <svg width="20" height="20" viewBox="0 0 16 16" fill="currentColor">
          <path
            d="M8 0c4.42 0 8 3.58 8 8a8.013 8.013 0 0 1-5.45 7.59c-.4.08-.55-.17-.55-.38 0-.27.01-1.13.01-2.2 0-.75-.25-1.23-.54-1.48 1.78-.2 3.65-.88 3.65-3.95 0-.88-.31-1.59-.82-2.15.08-.2.36-1.02-.08-2.12 0 0-.67-.22-2.2.82-.64-.18-1.32-.27-2-.27-.68 0-1.36.09-2 .27-1.53-1.03-2.2-.82-2.2-.82-.44 1.1-.16 1.92-.08 2.12-.51.56-.82 1.28-.82 2.15 0 3.06 1.86 3.75 3.64 3.95-.23.2-.44.55-.51 1.07-.46.21-1.61.55-2.33-.66-.15-.24-.6-.83-1.23-.82-.67.01-.27.38.01.53.34.19.73.9.82 1.13.16.45.68 1.31 2.69.94 0 .67.01 1.3.01 1.49 0 .21-.15.45-.55.38A7.995 7.995 0 0 1 0 8c0-4.42 3.58-8 8-8Z"
          />
        </svg>
        Sign in with GitHub
      </a>
    </div>
  </div>
</template>

<script setup>
const nodes = [
  // Data Sources (blue) — top
  { label: "Weather", x: 200, y: 35, color: "#0969da", w: 72 },
  { label: "Open-Meteo", x: 296, y: 65, color: "#0969da", w: 88 },
  // Analytics (purple) — right
  { label: "Charts", x: 328, y: 142, color: "#8250df", w: 58 },
  { label: "Forecasts", x: 313, y: 230, color: "#8250df", w: 78 },
  { label: "Trends", x: 245, y: 287, color: "#8250df", w: 60 },
  // Platform (green) — left
  { label: "Copilot", x: 156, y: 287, color: "#2da44e", w: 62 },
  { label: "Azure", x: 87, y: 230, color: "#2da44e", w: 54 },
  { label: "GitHub", x: 72, y: 142, color: "#2da44e", w: 58 },
  { label: "FinOps", x: 104, y: 65, color: "#2da44e", w: 58 },
];
</script>

<style scoped>
.login-screen {
  display: flex;
  align-items: center;
  justify-content: center;
  min-height: 100vh;
  padding: 1rem;
  background: #ffffff;
}

.login-content {
  text-align: center;
  width: 100%;
}

.hub-svg {
  width: 75vmin;
  margin: 0 auto 1rem;
  display: block;
}

.tagline {
  color: #656d76;
  font-size: 1.35rem;
  margin-top: 1rem;
  margin-bottom: 1rem;
  font-weight: 400;
}

.github-btn {
  display: inline-flex;
  align-items: center;
  gap: 0.5rem;
  background: #1f2328;
  color: white;
  border: none;
  border-radius: 8px;
  padding: 0.85rem 1.75rem;
  font-size: 1rem;
  font-weight: 500;
  text-decoration: none;
  cursor: pointer;
  transition: all 0.2s;
  box-shadow: 0 2px 8px rgba(0, 0, 0, 0.1);
}

.github-btn:hover {
  background: #2da44e;
  box-shadow: 0 4px 16px rgba(45, 164, 78, 0.3);
  transform: translateY(-1px);
}
</style>
