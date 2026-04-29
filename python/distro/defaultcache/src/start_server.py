import asyncio
import logging
from os import environ

from aiohttp.web import Application, Request, Response, run_app
from microsoft_agents.hosting.aiohttp import (
    CloudAdapter,
    start_agent_process,
)
from microsoft_agents.hosting.core import AgentApplication, AgentAuthConfiguration
from microsoft.opentelemetry import use_microsoft_opentelemetry

from microsoft.opentelemetry.a365.hosting.middleware.observability_hosting_manager import (
    ObservabilityHostingManager,
    ObservabilityHostingOptions,
)
from microsoft.opentelemetry.a365.hosting.token_cache_helpers import AgenticTokenCache

logger = logging.getLogger(__name__)


def create_token_resolver(token_cache: AgenticTokenCache):
    """Create a sync token resolver backed by AgenticTokenCache.

    The A365 exporter calls this from a background thread (BatchSpanProcessor),
    so we use asyncio.run() to bridge into the async token exchange.
    """

    def token_resolver(agent_id: str, tenant_id: str) -> str | None:
        try:
            token = asyncio.run(token_cache.get_observability_token(agent_id, tenant_id))
            if token:
                logger.info(f"Token resolved for agent_id: {agent_id}, tenant_id: {tenant_id}")
                return token.token
            logger.warning(f"No token found for agent_id: {agent_id}, tenant_id: {tenant_id}")
            return None
        except Exception as e:
            logger.error(f"Error resolving token for agent {agent_id}, tenant {tenant_id}: {e}")
            return None

    return token_resolver


def start_server(agent_application: AgentApplication, auth_configuration: AgentAuthConfiguration):
    async def entry_point(req: Request) -> Response:
        agent: AgentApplication = req.app["agent_app"]
        adapter: CloudAdapter = req.app["adapter"]
        return await start_agent_process(
            req,
            agent,
            adapter,
        )

    app = Application()
    app.router.add_post("/api/messages", entry_point)
    app["agent_configuration"] = auth_configuration
    app["agent_app"] = agent_application
    app["adapter"] = agent_application.adapter

    # Step 1: Create AgenticTokenCache — the built-in token cache
    token_cache = AgenticTokenCache()
    app["token_cache"] = token_cache

    # Make token cache available to agent handlers via application storage
    agent_application.adapter.app_context = {"token_cache": token_cache}

    # Step 2: Enable the A365 HTTP exporter
    environ["ENABLE_A365_OBSERVABILITY_EXPORTER"] = "true"

    # Step 3: Create token resolver backed by AgenticTokenCache
    token_resolver_func = create_token_resolver(token_cache)

    # Step 4: Configure the distro with the token resolver
    use_microsoft_opentelemetry(
        enable_azure_monitor=False,
        enable_a365=True,
        enable_console=True,
        a365_token_resolver=token_resolver_func,
        instrumentation_options={
            "requests": {"enabled": False},
            "urllib": {"enabled": False},
            "urllib3": {"enabled": False},
        },
    )

    # Step 5: Register baggage + output logging middleware
    ObservabilityHostingManager.configure(
        agent_application.adapter.middleware_set, ObservabilityHostingOptions(True, True)
    )

    try:
        run_app(app, host="localhost", port=int(environ.get("PORT", 3978)))
    except Exception as error:
        raise error
