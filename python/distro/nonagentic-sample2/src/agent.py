# Non-agentic token acquisition sample — S2S + OBO token + span export.
# TOKEN_MODE=obo (default): uses adapter pipeline with auth handler (requires dev tunnel)
# TOKEN_MODE=s2s: bypasses adapter, handles HTTP POST directly (works with emulator)

import json
import logging
from base64 import b64decode
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
    InvokeAgentScope,
    InvokeAgentScopeDetails,
    Request,
)

from utils.token_cache import cache_token, get_cached_token

logger = logging.getLogger(__name__)

load_dotenv()
agents_sdk_config = load_configuration_from_env(environ)

STORAGE = MemoryStorage()
CONNECTION_MANAGER = MsalConnectionManager(**agents_sdk_config)
ADAPTER = CloudAdapter(connection_manager=CONNECTION_MANAGER)
AUTHORIZATION = Authorization(STORAGE, CONNECTION_MANAGER, **agents_sdk_config)

AGENT_APP = AgentApplication[TurnState](
    storage=STORAGE,
    adapter=ADAPTER,
    authorization=AUTHORIZATION,
    **agents_sdk_config,
)

CLIENT_ID = environ.get("CONNECTIONS__SERVICE_CONNECTION__SETTINGS__CLIENTID", "")
TENANT_ID = environ.get("CONNECTIONS__SERVICE_CONNECTION__SETTINGS__TENANTID", "")


def _decode_jwt(token: str | None) -> dict:
    if not token:
        return {}
    try:
        payload = token.split(".")[1]
        payload += "=" * (4 - len(payload) % 4)
        return json.loads(b64decode(payload))
    except Exception:
        return {}


def _build_response(user_message: str, token_mode: str, token: str | None,
                    agent_id: str, tenant_id: str) -> str:
    """Build response text and create InvokeAgentScope span."""
    token_decoded = _decode_jwt(token)

    agent_details = AgentDetails(
        agent_id=agent_id,
        agent_name="NonAgenticSample",
        agent_description="Non-agentic token acquisition + span export sample",
        tenant_id=tenant_id,
    )

    request = Request(content=user_message, session_id=None)

    response_text = ""
    try:
        with BaggageBuilder().tenant_id(tenant_id).agent_id(agent_id).build():
            invoke_scope = InvokeAgentScope.start(
                request=request,
                scope_details=InvokeAgentScopeDetails(),
                agent_details=agent_details,
            )
            with invoke_scope:
                invoke_scope.record_input_messages([user_message])
                response_text = (
                    f"You said: {user_message}\n\n"
                    f"**({token_mode})** token={len(token) if token else 0} chars\n\n"
                    f"**appid**={token_decoded.get('appid', token_decoded.get('azp', 'n/a'))}\n\n"
                    f"**tid**={token_decoded.get('tid', 'n/a')}\n\n"
                    f"**aud**={token_decoded.get('aud', 'n/a')}\n\n"
                    f"**agentId**={agent_id}\n\n**tenantId**={tenant_id}"
                )
                invoke_scope.record_output_messages([response_text])
                logger.info(response_text)
    except Exception as e:
        response_text = f"Error creating observability scope: {e}"
        logger.error(response_text)

    return response_text


# ── Signout handler ───────────────────────────────────────────────────────

@AGENT_APP.message("logout", auth_handlers=["OBOCONNECTIONPROFILE"])
async def on_signout(context: TurnContext, _state: TurnState):
    await AGENT_APP.auth.sign_out(context)
    await context.send_activity("You have signed out")


# ── OBO handler (via adapter, for dev tunnel) ──────────────────────────────

@AGENT_APP.activity("message", auth_handlers=["OBOCONNECTIONPROFILE"])
async def on_message(context: TurnContext, _state: TurnState):
    """OBO flow — uses AGENT_APP.auth.get_token (same pattern as SDK obo-authorization sample)."""
    user_message = context.activity.text
    if not user_message:
        return

    agent_id = CLIENT_ID or "unknown"
    tenant_id = TENANT_ID or "unknown"

    token_response = await AGENT_APP.auth.get_token(context, "OBOCONNECTIONPROFILE")
    decoded = _decode_jwt(token_response.token)
    logger.info(f"[OBO] Token claims: aud={decoded.get('aud')}, name={decoded.get('name')}, upn={decoded.get('upn')}, scp={decoded.get('scp')}, tid={decoded.get('tid')}")

    cache_token(agent_id, tenant_id, token_response.token)

    response_text = _build_response(user_message, "obo", token_response.token, agent_id, tenant_id)

    try:
        await context.send_activity(response_text)
    except Exception as e:
        logger.info(f"[sendActivity failed] {e}")


# ── S2S handler (direct HTTP, for emulator) ────────────────────────────────

async def _acquire_s2s_token(agent_id: str, tenant_id: str) -> str | None:
    try:
        connection = CONNECTION_MANAGER.get_connection("SERVICE_CONNECTION")
        token = await connection.get_access_token(
            resource_url="https://login.microsoftonline.com",
            scopes=["api://9b975845-388f-4429-889e-eab1ef63949c/.default"],
        )
        if token:
            cache_token(agent_id, tenant_id, token)
            logger.info(f"[S2S] Token acquired: agentId={agent_id}, tenantId={tenant_id}, len={len(token)}")
        else:
            logger.info("[S2S] Token was empty")
        return token
    except Exception as e:
        logger.error(f"[S2S] Token acquisition failed: {e}")
        return None


async def on_message_direct(activity: dict) -> str:
    """S2S handler — bypasses adapter pipeline, works with emulator."""
    user_message = activity.get("text", "").strip()
    if not user_message:
        return "No message text"

    agent_id = CLIENT_ID or "unknown"
    tenant_id = TENANT_ID or "unknown"

    token = await _acquire_s2s_token(agent_id, tenant_id)
    return _build_response(user_message, "s2s", token, agent_id, tenant_id)


@AGENT_APP.error
async def on_error(context: TurnContext, error: Exception):
    logger.error("[on_turn_error] unhandled error: %s", error)
    await context.send_activity("The agent encountered an error.")
