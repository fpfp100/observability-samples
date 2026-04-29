# Using the Built-in AgenticTokenCache

This sample shows how to use `AgenticTokenCache` to provide Bearer tokens for the A365 exporter via OBO (On-Behalf-Of) token exchange.

## Setup

### 1. Create the cache and token resolver (start_server.py)

```python
from microsoft.opentelemetry.a365.hosting.token_cache_helpers import AgenticTokenCache

token_cache = AgenticTokenCache()
agent_application.adapter.app_context = {"token_cache": token_cache}

def token_resolver(agent_id: str, tenant_id: str) -> str | None:
    token = asyncio.run(token_cache.get_observability_token(agent_id, tenant_id))
    return token.token if token else None
```

### 2. Enable exporter and pass resolver

```python
environ["ENABLE_A365_OBSERVABILITY_EXPORTER"] = "true"

use_microsoft_opentelemetry(
    enable_a365=True,
    a365_token_resolver=token_resolver,
)
```

### 3. Register the TurnContext on every message (agent.py)

Must happen **before** creating any scopes:

```python
from microsoft.opentelemetry.a365.hosting.token_cache_helpers import AgenticTokenStruct

await token_cache.register_observability(
    agent_id=agent_details.agent_id,
    tenant_id=agent_details.tenant_id,
    token_generator=AgenticTokenStruct(
        authorization=AGENT_APP.auth,
        turn_context=context,
        auth_handler_name="AGENTIC",
    ),
    observability_scopes=get_observability_authentication_scope(),
)
```

`register_observability()` stores the `TurnContext` + `Authorization`. The actual OBO token exchange happens later when the exporter calls `get_observability_token()`. First registration per agent/tenant wins.
