import logging

logger = logging.getLogger(__name__)
_token_cache: dict[str, str] = {}


def cache_token(agent_id: str, tenant_id: str, token: str) -> None:
    key = f"{agent_id}:{tenant_id}"
    _token_cache[key] = token
    logger.info(f"Token cached for {key} (length={len(token)})")


def get_cached_token(agent_id: str, tenant_id: str) -> str | None:
    return _token_cache.get(f"{agent_id}:{tenant_id}")
