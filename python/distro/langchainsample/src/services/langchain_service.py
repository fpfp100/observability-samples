# Copyright (c) Microsoft Corporation. All rights reserved.
# Licensed under the MIT License.

"""
LangChain service with Azure OpenAI integration.

Uses LangChainInstrumentor for auto-instrumented observability tracing,
so no manual InferenceScope is needed here.
"""

import logging
from os import environ

from langchain_openai import AzureChatOpenAI
from langchain_core.messages import HumanMessage, SystemMessage
from microsoft_agents.hosting.core import TurnContext

logger = logging.getLogger(__name__)


async def call_langchain(user_message: str, context: TurnContext) -> str:
    """Make a call to Azure OpenAI via LangChain with the user's message.

    The LangChainInstrumentor (configured in start_server.py) automatically
    traces this invocation, so no manual InferenceScope is required.
    """
    try:
        deployment = environ.get("AZURE_OPENAI_DEPLOYMENT", "gpt-4o")
        llm = AzureChatOpenAI(
            azure_deployment=deployment,
            model=deployment,  # Workaround P-7: openai-v2 instrumentor crashes if model is None
            azure_endpoint=environ.get("AZURE_OPENAI_ENDPOINT"),
            api_key=environ.get("AZURE_OPENAI_API_KEY"),
            api_version="2025-01-01-preview",
        )

        messages = [
            SystemMessage(
                content="You are a helpful AI assistant. Provide clear, concise responses to user questions."
            ),
            HumanMessage(content=user_message),
        ]

        response = await llm.ainvoke(messages)
        return response.content

    except Exception as e:
        logger.error(f"Error calling LangChain with Azure OpenAI: {e}")
        return f"Sorry, I encountered an error while processing your request: {str(e)}"
