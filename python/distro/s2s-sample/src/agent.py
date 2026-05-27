# Copyright (c) Microsoft Corporation. All rights reserved.
# Licensed under the MIT License.

"""
S2S Observability Echo Agent.

Demonstrates Server-to-Server token acquisition for the Agent365 observability
exporter using the agentic identity chain. On each incoming message the agent:

  1. Acquires an S2S observability token via get_agentic_s2s_token
     (get_agentic_application_token + MSAL acquire_token_for_client)
  2. Caches it for the OTel exporter's token resolver
  3. Creates manual InvokeAgentScope / InferenceScope / OutputScope spans
  4. Echoes the user message back

Note: AgenticUserAuthorization.get_token returns a token scoped to
5a807f24-.../.default (bot framework), not the observability resource.
The S2S endpoint requires api://9b975845-.../.default, so the manual
two-step flow is needed.
"""

import logging
from os import environ

from dotenv import load_dotenv
from microsoft_agents.activity import load_configuration_from_env
from microsoft_agents.authentication.msal import MsalConnectionManager
from microsoft_agents.hosting.aiohttp import CloudAdapter
from microsoft_agents.hosting.core import (
    AgentApplication,
    Authorization,
    MemoryStorage,
    TurnContext,
    TurnState,
)
from microsoft.opentelemetry.a365.core import (
    AgentDetails,
    BaggageBuilder,
    InferenceCallDetails,
    InferenceOperationType,
    InferenceScope,
    InvokeAgentScope,
    InvokeAgentScopeDetails,
    OutputScope,
    Request,
)
from microsoft.opentelemetry.a365.core.models.response import Response

from utils.s2s_token import get_agentic_s2s_token
from utils.token_cache import cache_s2s_token

logger = logging.getLogger(__name__)

load_dotenv()
agents_sdk_config = load_configuration_from_env(environ)

STORAGE = MemoryStorage()
CONNECTION_MANAGER = MsalConnectionManager(**agents_sdk_config)
ADAPTER = CloudAdapter(connection_manager=CONNECTION_MANAGER)
AUTHORIZATION = Authorization(STORAGE, CONNECTION_MANAGER, **agents_sdk_config)

AGENT_APP = AgentApplication[TurnState](
    storage=STORAGE, adapter=ADAPTER, authorization=AUTHORIZATION, **agents_sdk_config
)


def _create_agent_details(context: TurnContext) -> AgentDetails:
    """Build AgentDetails from the incoming activity.

    Uses SDK helpers (get_agentic_instance_id/get_agentic_tenant_id) which read
    from recipient. Falls back to from_property for emulator compatibility where
    the agentic identity is on the sender.
    """
    agent_id = (
        context.activity.get_agentic_instance_id()
        or getattr(context.activity.from_property, "agentic_app_id", None)
    )
    tenant_id = (
        context.activity.get_agentic_tenant_id()
        or (context.activity.conversation.tenant_id if context.activity.conversation else None)
        or environ.get("TENANT_ID", "default-tenant")
    )

    return AgentDetails(
        agent_id=agent_id,
        agent_name=environ.get("AGENT_NAME", "S2S Echo Agent"),
        agent_description="Echo agent with S2S observability",
        tenant_id=tenant_id,
    )


@AGENT_APP.activity("message")
async def on_message(context: TurnContext, _state: TurnState):
    """Handle incoming messages: acquire S2S token, emit OTel scopes, echo back."""
    user_message = context.activity.text
    if not user_message:
        await context.send_activity("I didn't receive any message text.")
        return

    agent_details = _create_agent_details(context)

    # --- S2S observability token acquisition (must happen BEFORE creating spans) ---
    try:
        connection = CONNECTION_MANAGER.get_connection("SERVICE_CONNECTION")
        token = await get_agentic_s2s_token(
            connection,
            tenant_id=agent_details.tenant_id,
            agent_id=agent_details.agent_id,
        )
        cache_s2s_token(agent_details.agent_id, agent_details.tenant_id, token)
    except Exception as e:
        logger.error(f"[S2S] Failed to acquire observability token: {str(e)[:200]}")

    # --- Observability scopes (manual spans) ---
    request = Request(
        content=user_message,
        session_id=context.activity.conversation.id if context.activity.conversation else None,
    )

    try:
        # Set baggage so the A365 exporter knows which agent/tenant to resolve tokens for
        with BaggageBuilder().tenant_id(agent_details.tenant_id).agent_id(agent_details.agent_id).build():
            invoke_scope = InvokeAgentScope.start(
                request=request,
                scope_details=InvokeAgentScopeDetails(),
                agent_details=agent_details,
            )

            try:
                with invoke_scope:
                    invoke_scope.record_input_messages([user_message])

                    # Simulated inference scope (no real LLM call -- just demonstrating span creation)
                    inference_scope = InferenceScope.start(
                        request=request,
                        details=InferenceCallDetails(
                            operationName=InferenceOperationType.CHAT,
                            model="echo",
                            providerName="local",
                        ),
                        agent_details=agent_details,
                    )
                    with inference_scope:
                        inference_scope.record_input_messages([user_message])
                        inference_scope.record_output_messages([user_message])

                    # Output scope
                    response_text = f"Echo: {user_message}"
                    output_scope = OutputScope.start(
                        request=request,
                        response=Response(messages=[response_text]),
                        agent_details=agent_details,
                    )
                    with output_scope:
                        output_scope.record_output_messages([response_text])

                    await context.send_activity(response_text)

            except Exception as e:
                logger.error(f"Error in on_message: {e}")
                invoke_scope.record_error(e)
                await context.send_activity("Error processing your message. Please try again.")
    except Exception as e:
        logger.error(f"Error setting up observability scope: {e}")
        # Fallback: just echo without observability
        await context.send_activity(f"Echo: {user_message}")


@AGENT_APP.error
async def on_error(context: TurnContext, error: Exception):
    logger.error("[on_turn_error] unhandled error: %s", error)
    await context.send_activity("The agent encountered an error.")
