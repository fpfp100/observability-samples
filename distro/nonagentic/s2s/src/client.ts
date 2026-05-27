// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

// OTel SDK is initialized in otel-init.ts (first import in index.ts).
// This file is imported as a side-effect from agent.ts (`import './client.js'`)
// to ensure it loads after otel-init.ts has registered the SDK.

export interface Client {
  invokeAgent(prompt: string): Promise<string>;
}
