# Copyright (c) Microsoft Corporation. All rights reserved.
# Licensed under the MIT License.

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
from microsoft_agents.hosting.core.storage import (
    ConsoleTranscriptLogger,
    TranscriptLoggerMiddleware,
)
from microsoft_agents_a365.observability.core.invoke_agent_details import InvokeAgentScopeDetails
from microsoft_agents_a365.observability.core.invoke_agent_scope import InvokeAgentScope
from microsoft_agents_a365.observability.core.middleware.baggage_builder import BaggageBuilder
from microsoft_agents_a365.observability.hosting.token_cache_helpers import (
    AgenticTokenStruct,
)
from microsoft_agents_a365.runtime.environment_utils import (
    get_observability_authentication_scope,
)
from services.agents_service import call_openai_agents
from utils.observability_helpers import (
    create_agent_details,
    create_request_details,
)
from utils.token_cache import cache_agentic_token

logger = logging.getLogger(__name__)

load_dotenv()
agents_sdk_config = load_configuration_from_env(environ)


STORAGE = MemoryStorage()
CONNECTION_MANAGER = MsalConnectionManager(**agents_sdk_config)
ADAPTER = CloudAdapter(connection_manager=CONNECTION_MANAGER)
ADAPTER.use(TranscriptLoggerMiddleware(ConsoleTranscriptLogger()))
AUTHORIZATION = Authorization(STORAGE, CONNECTION_MANAGER, **agents_sdk_config)

AGENT_APP = AgentApplication[TurnState](
    storage=STORAGE, adapter=ADAPTER, authorization=AUTHORIZATION, **agents_sdk_config
)


def _get_token_cache():
    """Get token cache from application context (injected by start_server)."""
    if hasattr(ADAPTER, "app_context") and "token_cache" in ADAPTER.app_context:
        return ADAPTER.app_context["token_cache"]
    return None


@AGENT_APP.activity("message", auth_handlers=["AGENTIC"])
async def on_message(context: TurnContext, _state: TurnState):
    """Handle incoming messages and respond using OpenAI Agents SDK."""
    agent_details = create_agent_details(context)

    token_cache = _get_token_cache()
    if token_cache:
        token_struct = AgenticTokenStruct(
            authorization=AGENT_APP.auth,
            turn_context=context,
            auth_handler_name="AGENTIC",
        )
        token_cache.register_observability(
            agent_id=agent_details.agent_id,
            tenant_id=agent_details.tenant_id,
            token_generator=token_struct,
            observability_scopes=get_observability_authentication_scope(),
        )
    else:
        logger.warning("Token cache not available in app context")

    invoke_scope_details = InvokeAgentScopeDetails()

    user_message = context.activity.text
    if not user_message:
        await context.send_activity("I didn't receive any message text to process.")
        return

    request_details = create_request_details(
        user_message, context.activity.conversation.id if context.activity.conversation else None
    )

    try:
        invoke_scope = InvokeAgentScope.start(
            request=request_details,
            scope_details=invoke_scope_details,
            agent_details=agent_details,
        )

        try:
            with invoke_scope:
                invoke_scope.record_input_messages([user_message])

                exaau_token = await AGENT_APP.auth.exchange_token(
                    context,
                    scopes=get_observability_authentication_scope(),
                    auth_handler_id="AGENTIC",
                )
                cache_agentic_token(
                    agent_details.tenant_id, agent_details.agent_id, exaau_token.token
                )

                logger.info(f"Processing user message: {user_message}")

                # Call OpenAI Agents SDK - tracing handled by OpenAIAgentsTraceInstrumentor
                ai_response = await call_openai_agents(user_message, context)

                await context.send_activity(ai_response)

        except Exception as e:
            logger.error(f"Error in on_message: {e}")
            invoke_scope.record_error(e)
            await context.send_activity(
                "I encountered an error while processing your message. Please try again."
            )
    except Exception as e:
        logger.error(f"Error setting up observability scope: {e}")


@AGENT_APP.error
async def on_error(context: TurnContext, error: Exception):
    logger.error("[on_turn_error] unhandled error: %s", error)
    await context.send_activity("The agent encountered an error.")
