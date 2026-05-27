# Copyright (c) Microsoft Corporation. All rights reserved.
# Licensed under the MIT License.

"""
Server setup with S2S observability exporter configuration.

Key difference from OBO samples: uses a365_use_s2s_endpoint=True which
changes the exporter endpoint path from /observability to /observabilityService.
"""

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

from utils.token_cache import get_cached_s2s_token

logger = logging.getLogger(__name__)


def create_s2s_token_resolver():
    """
    Create a token resolver that returns cached S2S tokens.

    The resolver is called by the A365 exporter when it needs a token to
    authenticate with the observability endpoint. Tokens are cached by the
    agent's on_message handler via get_agentic_s2s_token.
    """

    def token_resolver(agent_id: str, tenant_id: str) -> str | None:
        try:
            logger.info(f"S2S token resolver called for agent_id={agent_id}, tenant_id={tenant_id}")
            token = get_cached_s2s_token(agent_id, tenant_id)
            if token:
                logger.info("S2S token resolver: cache hit")
            else:
                logger.warning(f"S2S token resolver: no cached token for {agent_id}:{tenant_id}")
            return token
        except Exception as e:
            logger.error(f"S2S token resolver error: {e}")
            return None

    return token_resolver


def start_server(agent_application: AgentApplication, auth_configuration: AgentAuthConfiguration):
    async def entry_point(req: Request) -> Response:
        agent: AgentApplication = req.app["agent_app"]
        adapter: CloudAdapter = req.app["adapter"]
        return await start_agent_process(req, agent, adapter)

    app = Application()
    app.router.add_post("/api/messages", entry_point)
    app["agent_configuration"] = auth_configuration
    app["agent_app"] = agent_application
    app["adapter"] = agent_application.adapter

    # Enable the A365 observability exporter via environment variable
    environ["ENABLE_A365_OBSERVABILITY_EXPORTER"] = "true"

    # Create the S2S token resolver
    token_resolver_func = create_s2s_token_resolver()

    # Configure Microsoft OpenTelemetry with S2S endpoint.
    # a365_use_s2s_endpoint=True switches the export path from
    # /observability to /observabilityService.
    use_microsoft_opentelemetry(
        enable_azure_monitor=False,
        enable_a365=True,
        enable_console=True,
        a365_token_resolver=token_resolver_func,
        a365_use_s2s_endpoint=True,
        a365_enable_observability_exporter=True,
    )

    # Register observability middleware (baggage propagation + output logging)
    ObservabilityHostingManager.configure(
        agent_application.adapter.middleware_set, ObservabilityHostingOptions(True, True)
    )

    try:
        run_app(app, host="localhost", port=3978)
    except Exception as error:
        raise error
