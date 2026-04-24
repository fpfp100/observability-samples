// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

// MUST be first: registers the OpenTelemetry SDK before any agents-a365-observability
// module is loaded transitively, and also calls configDotenv().
import './otel-init.js';

import { AuthConfiguration, authorizeJWT, CloudAdapter, loadAuthConfigFromEnv, Request } from '@microsoft/agents-hosting';
import express, { Response, Express } from 'express'
import { agentApplication } from './agent.js';
import { ObservabilityHostingManager } from '@microsoft/opentelemetry';

// Use request validation middleware only if hosting publicly
const isProduction = Boolean(process.env.WEBSITE_SITE_NAME) || process.env.NODE_ENV === 'production';
const authConfig: AuthConfiguration = isProduction ? loadAuthConfigFromEnv() : {};

// Register observability middleware on the adapter
const adapter = agentApplication.adapter as CloudAdapter;
const observabilityManager = new ObservabilityHostingManager();
observabilityManager.configure(adapter as unknown as { use(...m: unknown[]): void }, { enableOutputLogging: true });

const server: Express = express()
server.use(express.json())
server.use(authorizeJWT(authConfig))

server.post('/api/messages', (req: Request, res: Response) => {
  adapter.process(req, res, async (context) => {
    await agentApplication.run(context)
  })
})

const port = 3978
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
