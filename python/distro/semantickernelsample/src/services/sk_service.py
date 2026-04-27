# Copyright (c) Microsoft Corporation. All rights reserved.
# Licensed under the MIT License.

"""
Semantic Kernel service with Azure OpenAI chat completion.

The SemanticKernelInstrumentor (configured in start_server.py) automatically
creates inference spans for all kernel invocations, so no manual
InferenceScope is needed here.
"""

import logging
from os import environ

import semantic_kernel as sk
from semantic_kernel.connectors.ai.open_ai import AzureChatCompletion
from semantic_kernel.contents import ChatHistory

from utils.azure_openai_client import get_deployment_name

logger = logging.getLogger(__name__)


async def call_semantic_kernel(user_message: str) -> str:
    """
    Process a user message using Semantic Kernel with Azure OpenAI.

    Creates a Kernel with AzureChatCompletion, builds a ChatHistory,
    and invokes chat completion. Inference telemetry is handled
    automatically by the SemanticKernelInstrumentor.
    """
    endpoint = environ.get("AZURE_OPENAI_ENDPOINT")
    api_key = environ.get("AZURE_OPENAI_API_KEY")
    deployment_name = get_deployment_name()

    if not endpoint:
        raise ValueError("AZURE_OPENAI_ENDPOINT environment variable is required")
    if not api_key:
        raise ValueError("AZURE_OPENAI_API_KEY environment variable is required")

    kernel = sk.Kernel()

    chat_service = AzureChatCompletion(
        deployment_name=deployment_name,
        endpoint=endpoint,
        api_key=api_key,
    )
    kernel.add_service(chat_service)

    chat_history = ChatHistory()
    chat_history.add_system_message(
        "You are a helpful AI assistant. Provide clear, concise responses to user questions."
    )
    chat_history.add_user_message(user_message)

    try:
        result = await kernel.invoke_prompt(user_message)
        return str(result)

    except Exception as e:
        logger.error(f"Error calling Semantic Kernel: {e}")
        return f"Sorry, I encountered an error while processing your request: {str(e)}"
