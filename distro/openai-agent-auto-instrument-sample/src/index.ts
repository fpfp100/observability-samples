// ------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// ------------------------------------------------------------------------------

// It is important to load environment variables before importing other modules
import { configDotenv } from 'dotenv';

configDotenv();

// Initialize observability as the first thing after env vars so auto-instrumentation
// hooks are registered before any @openai/agents code executes.
import './Telemetry';
import { shutdownMicrosoftOpenTelemetry } from '@microsoft/opentelemetry';

import { AuthConfiguration, authorizeJWT, CloudAdapter, loadAuthConfigFromEnv, Request } from '@microsoft/agents-hosting';
import express, { Response } from 'express';
import { agentApplication } from './A365Agent';
import { ObservabilityHostingManager } from '@microsoft/opentelemetry';

// Use request validation middleware only if hosting publicly
const isProduction = Boolean(process.env.WEBSITE_SITE_NAME) || process.env.NODE_ENV === 'production';
const authConfig: AuthConfiguration = isProduction ? loadAuthConfigFromEnv() : {};
const adapter = new CloudAdapter(authConfig);

// Register observability middleware on the adapter
const observabilityHostingManager = new ObservabilityHostingManager();
observabilityHostingManager.configure(
  adapter as unknown as { use(...m: unknown[]): void },
  { enableOutputLogging: true },
);

const app = express();
app.use(express.json());
// eslint-disable-next-line @typescript-eslint/no-explicit-any
app.use(authorizeJWT(authConfig) as any);

app.post('/api/messages', async (req: Request, res: Response) => {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  await adapter.process(req, res as any, async (context) => {
    const app = agentApplication;
    await app.run(context);
  });
});

const port = process.env.PORT || 3978;
const server = app.listen(port, () => {
  console.log(`\nServer listening to port ${port} for appId ${authConfig.clientId} debug ${process.env.DEBUG}`);
}).on('error', async (err) => {
  console.error(err);
  await shutdownMicrosoftOpenTelemetry();
  process.exit(1);
}).on('close', async () => {
  console.log('Observability is shutting down...');
  await shutdownMicrosoftOpenTelemetry();
});

process.on('SIGINT', () => {
  console.log('Received SIGINT. Shutting down gracefully...');
  server.close(() => {
    console.log('Server closed.');
    process.exit(0);
  });
});
