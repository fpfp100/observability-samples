# Copyright (c) Microsoft Corporation. All rights reserved.
# Licensed under the MIT License.

"""
Simple token cache for S2S observability exporter authentication.
"""

import logging

logger = logging.getLogger(__name__)

# Global token cache: key = "{agent_id}:{tenant_id}" -> token string
_s2s_token_cache: dict[str, str] = {}


def cache_s2s_token(agent_id: str, tenant_id: str, token: str) -> None:
    """Cache the S2S observability token for use by the OTel exporter."""
    key = f"{agent_id}:{tenant_id}"
    _s2s_token_cache[key] = token
    logger.info(f"S2S token cached for {key} (length={len(token)})")


def get_cached_s2s_token(agent_id: str, tenant_id: str) -> str | None:
    """Retrieve cached S2S observability token."""
    key = f"{agent_id}:{tenant_id}"
    token = _s2s_token_cache.get(key)
    if not token:
        logger.debug(f"S2S token cache miss for {key}")
    return token
