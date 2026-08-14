# Live Interpreter

.NET 8 isolated Azure Functions + React/Vite for live audio-to-English interpretation with Azure AI Foundry `gpt-realtime-translate`.

The Function creates a short-lived realtime client secret. React then streams microphone audio directly to Azure over WebRTC and plays the returned English audio; the long-lived Azure credential never reaches the browser.

## Run locally

Prerequisites: .NET 8, Azure Functions Core Tools v4, Node.js 20+, and a Global Standard `gpt-realtime-translate` deployment.

1. Copy `api/local.settings.example.json` to `api/local.settings.json`. Set the endpoint, deployment, and (for local development) API key. Alternatively leave the key empty, run `az login`, and use `DefaultAzureCredential`.
2. Run `func start` from `api`.
3. Run `npm install`, then `npm run dev`, from `web`.
4. Open `http://localhost:5173`. Use headphones to avoid translated audio feeding back into the microphone.

For Azure, deploy `api` to Functions and `web/dist` to Static Web Apps. Prefer a system-assigned managed identity with the Cognitive Services OpenAI User role and omit the API key. Configure Static Web Apps to route `/api`, restrict Function CORS to the web origin, and add authentication/rate limiting before public use. HTTPS is required for browser microphone access outside localhost.

```text
React -> Function /api/realtime/session -> Foundry client secret
React ================= WebRTC =================> Foundry
       microphone audio          English audio + transcript
```

The implementation uses the GA `/openai/v1/realtime/client_secrets` and `/openai/v1/realtime/calls` endpoints.
