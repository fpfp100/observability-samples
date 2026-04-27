# Copyright (c) Microsoft Corporation. All rights reserved.
# Licensed under the MIT License.

"""
OpenAI Agents SDK service.

Uses the OpenAI Agents SDK to process messages. Auto-instrumentation is handled
by OpenAIAgentsInstrumentor (configured in start_server.py), so there is no need
for manual InferenceScope tracing here.
"""

import logging
from os import environ

from agents import Agent, Runner

logger = logging.getLogger(__name__)


async def call_openai_agents(user_message: str) -> str:
    """
    Run the OpenAI Agents SDK with the user's message.

    The OpenAI Agents SDK uses OPENAI_API_KEY by default. For Azure OpenAI,
    set the appropriate environment variables (see env.TEMPLATE).

    Auto-instrumentation via OpenAIAgentsInstrumentor will automatically create
    spans for agent runs, LLM calls, and tool executions.
    """
    model = environ.get("AZURE_OPENAI_DEPLOYMENT", "gpt-4o")

    agent = Agent(
        name="Assistant",
        instructions="You are a helpful AI assistant. Provide clear, concise responses to user questions.",
        model=model,
    )

    try:
        logger.info(f"Running OpenAI Agent with message: {user_message}")
        result = await Runner.run(agent, user_message)
        logger.info("OpenAI Agent run completed successfully")
        return result.final_output
    except Exception as e:
        logger.error(f"Error running OpenAI Agent: {e}")
        return f"Sorry, I encountered an error while processing your request: {str(e)}"
