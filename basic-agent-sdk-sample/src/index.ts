// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

// It is important to load environment variables before importing other modules
import { configDotenv } from 'dotenv';

configDotenv();

import { AuthConfiguration, authorizeJWT, CloudAdapter, loadAuthConfigFromEnv, Request } from '@microsoft/agents-hosting';
import express, { Response, Express } from 'express'
import { agentApplication } from './agent.js';
import { ObservabilityHostingManager } from '@microsoft/agents-a365-observability-hosting';

// Use request validation middleware only if hosting publicly
const isProduction = Boolean(process.env.WEBSITE_SITE_NAME) || process.env.NODE_ENV === 'production';
const authConfig: AuthConfiguration = loadAuthConfigSafely(isProduction);

// Register observability middleware on the adapter
const adapter = agentApplication.adapter as CloudAdapter;
const observabilityManager = new ObservabilityHostingManager();
observabilityManager.configure(adapter, { enableBaggage: true, enableOutputLogging: true });

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
