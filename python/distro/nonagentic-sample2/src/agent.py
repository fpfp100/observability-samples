# Non-agentic token acquisition sample — mirrors Node.js test-agents/agentic-ai/index.ts.
# TOKEN_MODE=s2s (default): connection.get_access_token → export to /observabilityService
# TOKEN_MODE=obo: authorization.exchange_token → export to /observability (requires dev tunnel)

import json
import jwt
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
from microsoft_agents.hosting.core.app.oauth.auth_handler import AuthHandler
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

# Non-agentic auth — AzureBotUserAuthorization (same as Node.js azureBotOAuthConnectionName)
AUTHORIZATION = Authorization(
    STORAGE,
    CONNECTION_MANAGER,
    auth_handlers={
        "agentic": AuthHandler(
            name="agentic",
            abs_oauth_connection_name="agentic",
            scopes=["https://graph.microsoft.com/.default"],
        ),
        "oboConnectionProfile": AuthHandler(
            name="oboConnectionProfile",
            abs_oauth_connection_name="oboConnectionProfile",
            scopes=[environ.get("oboConnectionProfile_scopes", "api://botid-b6e564b2-c909-452d-9e9f-9a48dfdfa043/default")],
        ),
    },
)

AGENT_APP = AgentApplication[TurnState](
    storage=STORAGE,
    adapter=ADAPTER,
    authorization=AUTHORIZATION,
    **agents_sdk_config,
)

CLIENT_ID = environ.get("CONNECTIONS__SERVICE_CONNECTION__SETTINGS__CLIENTID", "")
CLIENT_SECRET = environ.get("CONNECTIONS__SERVICE_CONNECTION__SETTINGS__CLIENTSECRET", "")
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


async def _test_s2s(agent_id: str, tenant_id: str) -> str | None:
    """Mirrors JS: connection.getAccessToken('api://9b975845-.../.default')"""
    try:
        connection = CONNECTION_MANAGER.get_connection("SERVICE_CONNECTION")
        token = await connection.get_access_token(
            resource_url="https://login.microsoftonline.com",
            scopes=["api://9b975845-388f-4429-889e-eab1ef63949c/.default"],
        )
        if token:
            cache_token(agent_id, tenant_id, token)
            logger.info(f"[S2S] Token acquired and cached: agentId={agent_id}, tenantId={tenant_id}, len={len(token)}")
        else:
            logger.info("[S2S] Token was empty")
        return token
    except Exception as e:
        logger.error(f"[S2S] Token acquisition failed: {e}")
        return None


async def _test_obo(context: TurnContext, user_token: str | None, agent_id: str, tenant_id: str) -> str | None:
    """Manual OBO exchange — Python SDK exchange_token doesn't do real OBO yet (TODO in source).
    Uses MSAL acquire_token_on_behalf_of directly, same as .NET ExchangeTurnTokenAsync."""
    if not user_token:
        logger.error("[OBO] No user token to exchange")
        return None
    try:
        import msal
        client_id = environ.get("CONNECTIONS__SERVICE_CONNECTION__SETTINGS__CLIENTID", "")
        client_secret = environ.get("CONNECTIONS__SERVICE_CONNECTION__SETTINGS__CLIENTSECRET", "")
        tenant = environ.get("CONNECTIONS__SERVICE_CONNECTION__SETTINGS__TENANTID", "")

        app = msal.ConfidentialClientApplication(
            client_id,
            authority=f"https://login.microsoftonline.com/{tenant}",
            client_credential=client_secret,
        )
        result = app.acquire_token_on_behalf_of(
            user_assertion=user_token,
            scopes=["api://9b975845-388f-4429-889e-eab1ef63949c/Agent365.Observability.OtelWrite"],
        )
        token = result.get("access_token")
        if token:
            cache_token(agent_id, tenant_id, token)
            logger.info(f"[OBO] Token exchanged and cached: agentId={agent_id}, tenantId={tenant_id}, len={len(token)}")
        else:
            logger.error(f"[OBO] Exchange failed: {result.get('error_description', result.get('error', 'unknown'))}")
        return token
    except Exception as e:
        logger.error(f"[OBO] Token exchange failed: {e}")
        return None


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
        # Set baggage so the Agent365 exporter knows which agent/tenant to resolve tokens for
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


# ── OBO handler (via adapter, for dev tunnel) ──────────────────────────────

@AGENT_APP.activity("message", auth_handlers=["oboConnectionProfile"])
async def on_message(context: TurnContext, _state: TurnState):
    """Non-agentic OBO — uses AGENT_APP.auth.get_token to get user token via Azure Bot OAuth."""
    user_message = context.activity.text
    if not user_message:
        return

    agent_id = CLIENT_ID or "unknown"
    tenant_id = TENANT_ID or "unknown"

    # Get user token via auth handler
    aau_token = await AGENT_APP.auth.get_token(context, "oboConnectionProfile")
    decoded = jwt.decode(aau_token.token, options={"verify_signature": False})
    logger.info(f"[OBO] Token claims: aud={decoded.get('aud')}, name={decoded.get('name')}, upn={decoded.get('upn')}, scp={decoded.get('scp')}, tid={decoded.get('tid')}")

    # Cache the token for the exporter
    cache_token(agent_id, tenant_id, aau_token.token)
    logger.info(f"[OBO] Token cached for agentId={agent_id}, tenantId={tenant_id}, len={len(aau_token.token)}")

    response_text = _build_response(user_message, "obo", aau_token.token, agent_id, tenant_id)

    try:
        await context.send_activity(response_text)
    except Exception as e:
        logger.info(f"[sendActivity failed] {e}")


# ── S2S handler (direct HTTP, for emulator) ────────────────────────────────

async def on_message_direct(activity: dict) -> str:
    """Handle message directly from HTTP POST — bypasses adapter pipeline.
    Python adapter calls get_agentic_instance_token during process_activity,
    which fails with the emulator. This handler works without the adapter."""

    user_message = activity.get("text", "").strip()
    if not user_message:
        return "No message text"

    agent_id = environ.get("CONNECTIONS__SERVICE_CONNECTION__SETTINGS__CLIENTID", "unknown")
    tenant_id = environ.get("CONNECTIONS__SERVICE_CONNECTION__SETTINGS__TENANTID", "unknown")

    token = await _test_s2s(agent_id, tenant_id)

    return _build_response(user_message, "s2s", token, agent_id, tenant_id)


@AGENT_APP.error
async def on_error(context: TurnContext, error: Exception):
    logger.error("[on_turn_error] unhandled error: %s", error)
    await context.send_activity("The agent encountered an error.")
