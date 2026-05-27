# Copyright (c) Microsoft Corporation. All rights reserved.
# Licensed under the MIT License.

import logging

# Configure root logger first
logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s - %(name)s - %(levelname)s - %(message)s (%(filename)s:%(lineno)d)",
    handlers=[logging.StreamHandler()],
)

ms_agents_logger = logging.getLogger("microsoft_agents")
console_handler = logging.StreamHandler()
console_handler.setFormatter(
    logging.Formatter(
        "%(asctime)s - %(name)s - %(levelname)s - %(message)s (%(filename)s:%(lineno)d)"
    )
)
ms_agents_logger.addHandler(console_handler)
ms_agents_logger.setLevel(logging.ERROR)

from agent import AGENT_APP, CONNECTION_MANAGER
from start_server import start_server

start_server(
    agent_application=AGENT_APP,
    auth_configuration=CONNECTION_MANAGER.get_default_connection_configuration(),
)
