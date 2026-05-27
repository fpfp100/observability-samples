// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

// MUST be first: load environment variables before importing other modules.
import './otel-init.js';

import { AuthConfiguration, authorizeJWT, CloudAdapter, loadAuthConfigFromEnv, Request } from '@microsoft/agents-hosting';
import { ObservabilityHostingManager } from '@microsoft/opentelemetry';
import express, { Response, Express } from 'express'
import { agentApplication } from './agent.js';

// Use request validation middleware only if hosting publicly
const isProduction = Boolean(process.env.WEBSITE_SITE_NAME) || process.env.NODE_ENV === 'production';
const authConfig: AuthConfiguration = loadAuthConfigSafely(isProduction);

const adapter = agentApplication.adapter as CloudAdapter;

// Register A365 observability middleware on the adapter:
//  - BaggageMiddleware: propagates A365 baggage across activities
//  - OutputLoggingMiddleware: emits output_messages spans for outgoing activities
// Cast: CloudAdapter.use signature differs slightly from the MiddlewareLike
// shape ObservabilityHostingManager.configure expects across SDK versions;
// the runtime contract (.use(...middlewares)) matches.
const observabilityHostingManager = new ObservabilityHostingManager();
observabilityHostingManager.configure(adapter as unknown as { use(...m: unknown[]): void }, {
  enableBaggage: true,
  enableOutputLogging: true,
});

const server: Express = express()
server.use(express.json())
if (isProduction && Object.keys(authConfig).length > 0) {
  server.use(authorizeJWT(authConfig))
}

server.post('/api/messages', (req: Request, res: Response) => {
  adapter.process(req, res, async (context) => {
    await agentApplication.run(context)
  })
})

const port = Number(process.env.PORT || 3978)
const host = isProduction ? '0.0.0.0' : '127.0.0.1';
server.listen(port, host, async () => {
  console.log(`\nServer listening on http://${host}:${port} for appId ${authConfig.clientId} debug ${process.env.DEBUG}`)
}).on('error', async (err: unknown) => {
  console.error(err);
  process.exit(1);
}).on('close', async () => {
  console.log('Server closed');
  process.exit(0);
});

function loadAuthConfigSafely(isProductionEnvironment: boolean): AuthConfiguration {
  if (!isProductionEnvironment) {
    return {};
  }

  try {
    return loadAuthConfigFromEnv();
  } catch (error) {
    const message = error instanceof Error ? error.message : String(error);
    console.warn(`[langchain-sample] Falling back to unauthenticated local mode: ${message}`);
    return {};
  }
}
