# Non-agentic token acquisition sample — S2S + OBO token + span export.
# TOKEN_MODE=obo (default): uses adapter pipeline with auth handler (requires dev tunnel)
# TOKEN_MODE=s2s: bypasses adapter, handles HTTP POST directly (works with emulator)

import logging
from os import environ

from aiohttp.web import Application, Request, Response, run_app
from microsoft_agents.hosting.aiohttp import start_agent_process, jwt_authorization_middleware, CloudAdapter
from microsoft_agents.hosting.core import AgentApplication, AgentAuthConfiguration
from microsoft.opentelemetry import use_microsoft_opentelemetry

from utils.token_cache import get_cached_token

logger = logging.getLogger(__name__)


def create_token_resolver():
    def token_resolver(agent_id: str, tenant_id: str) -> str | None:
        token = get_cached_token(agent_id, tenant_id)
        logger.info(f"[TokenResolver] agentId={agent_id}, tenantId={tenant_id}, hit={token is not None}, len={len(token) if token else 0}")
        return token
    return token_resolver


def start_server(agent_application: AgentApplication, auth_configuration: AgentAuthConfiguration):
    token_mode = environ.get("TOKEN_MODE", "obo").lower()
    use_s2s = token_mode == "s2s"

    if use_s2s:
        from agent import on_message_direct

        async def entry_point(req: Request) -> Response:
            try:
                body = await req.json()
                if body.get("type") != "message":
                    return Response(status=200, text="OK")
                result = await on_message_direct(body)
                return Response(status=200, text=result, content_type="text/plain")
            except Exception as e:
                logger.error(f"Error processing message: {e}", exc_info=True)
                return Response(status=500, text=str(e))

        app = Application()
    else:
        # OBO: standard adapter pipeline (matches SDK obo-authorization sample)
        async def entry_point(req: Request) -> Response:
            agent: AgentApplication = req.app["agent_app"]
            adapter: CloudAdapter = req.app["adapter"]
            return await start_agent_process(req, agent, adapter)

        app = Application(middlewares=[jwt_authorization_middleware])

    app.router.add_post("/api/messages", entry_point)
    app["agent_configuration"] = auth_configuration
    app["agent_app"] = agent_application
    app["adapter"] = agent_application.adapter

    # Configure observability distro
    environ["ENABLE_A365_OBSERVABILITY_EXPORTER"] = "true"

    use_microsoft_opentelemetry(
        enable_azure_monitor=False,
        enable_a365=True,
        enable_console=True,
        a365_token_resolver=create_token_resolver(),
        a365_use_s2s_endpoint=use_s2s,
        a365_enable_observability_exporter=True,
    )

    logger.info(f"Observability configured: mode={token_mode}, s2s_endpoint={use_s2s}")

    port = int(environ.get("PORT", "3978"))
    logger.info(f"Starting non-agentic sample on port {port} (mode={token_mode})")

    try:
        run_app(app, host="localhost", port=port)
    except Exception as error:
        raise error
