<template>
  <div class="login-screen">
    <div class="login-card">
      <h1 class="login-title">Azure FinOps Agent</h1>
      <p class="login-sub">
        Sign in to explore Azure pricing, compare costs, and get FinOps
        insights.
      </p>
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

<script setup></script>

<style scoped>
.login-screen {
  display: flex;
  align-items: center;
  justify-content: center;
  min-height: 100vh;
  background: #fafbfc;
}

.login-card {
  text-align: center;
  max-width: 400px;
  padding: 3rem 2rem;
}

.login-title {
  font-size: 1.75rem;
  font-weight: 700;
  color: #1f2328;
  margin: 0 0 0.75rem;
}

.login-sub {
  font-size: 1rem;
  color: #656d76;
  margin: 0 0 2rem;
  line-height: 1.5;
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
}

.github-btn:hover {
  background: #2da44e;
  transform: translateY(-1px);
}
</style>
