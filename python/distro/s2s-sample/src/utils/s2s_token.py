# Copyright (c) Microsoft Corporation. All rights reserved.
# Licensed under the MIT License.

"""
S2S token acquisition using the agentic identity chain.

Flow:
  1. Get an agentic application token via the service connection
     (client_credentials + fmi_path).
  2. Use that token as a client_assertion with ConfidentialClientApplication
     to acquire an observability-scoped token for:
       api://9b975845-388f-4429-889e-eab1ef63949c/.default
"""

import asyncio
import logging

from msal import ConfidentialClientApplication
from microsoft_agents.hosting.core import AccessTokenProviderBase

logger = logging.getLogger(__name__)

# The observability resource scope for client_credentials (S2S).
# This is the /.default scope for the Agent365 Observability app.
OBSERVABILITY_S2S_SCOPE = "api://9b975845-388f-4429-889e-eab1ef63949c/.default"


async def get_agentic_s2s_token(
    connection: AccessTokenProviderBase,
    tenant_id: str,
    agent_id: str,
    scopes: list[str] | None = None,
) -> str:
    """
    Acquire an S2S token for the given scopes using the agentic identity chain.

    Same pattern as MsalAuth.get_agentic_instance_token but targeting the
    observability scope instead of AzureAdTokenExchange.

    Args:
        connection: The AccessTokenProviderBase (MsalAuth) for the service connection.
        tenant_id: The tenant ID.
        agent_id: The agent's agentic app instance ID.
        scopes: Token scopes. Defaults to the observability S2S scope.

    Returns:
        The access token string.

    Raises:
        ValueError: If token acquisition fails.
    """
    scopes = scopes or [OBSERVABILITY_S2S_SCOPE]

    # Step 1: Get the agentic application token (client_credentials + fmi_path)
    app_token = await connection.get_agentic_application_token(tenant_id, agent_id)
    if not app_token:
        raise ValueError(f"Failed to acquire agentic application token for agent {agent_id}")

    # Step 2: Use the app token as client_assertion to get an observability-scoped token
    authority = f"https://login.microsoftonline.com/{tenant_id}"
    cca = ConfidentialClientApplication(
        client_id=agent_id,
        authority=authority,
        client_credential={"client_assertion": app_token},
    )

    result = await asyncio.to_thread(
        lambda: cca.acquire_token_for_client(scopes=scopes)
    )

    if not result or "access_token" not in result:
        error_desc = result.get("error_description", str(result)) if result else "No result"
        raise ValueError(f"Failed to acquire S2S observability token: {error_desc}")

    return result["access_token"]
