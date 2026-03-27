import { ApplicationInsights } from "@microsoft/applicationinsights-web";
import { createApp } from "vue";
import App from "./App.vue";

// Initialize Application Insights for frontend telemetry (page views, exceptions, user IP/country)
fetch("/api/config")
  .then((r) => r.json())
  .then((config) => {
    if (config.appInsightsConnectionString) {
      const appInsights = new ApplicationInsights({
        config: {
          connectionString: config.appInsightsConnectionString,
          enableAutoRouteTracking: true,
          enableCorsCorrelation: true,
          enableRequestHeaderTracking: true,
          enableResponseHeaderTracking: true,
        },
      });
      appInsights.loadAppInsights();
      appInsights.trackPageView();
      window.__appInsights = appInsights;
    }
  })
  .catch(() => {});

createApp(App).mount("#app");
