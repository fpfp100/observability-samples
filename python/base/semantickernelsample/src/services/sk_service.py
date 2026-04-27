import logging
import os
import semantic_kernel as sk
from semantic_kernel.connectors.ai.open_ai import AzureChatCompletion, AzureChatPromptExecutionSettings
from semantic_kernel.contents import ChatHistory
from microsoft_agents.hosting.core import TurnContext
from utils.azure_openai_client import get_deployment_name

logger = logging.getLogger(__name__)


async def call_semantic_kernel(user_message: str, context: TurnContext) -> str:
    try:
        kernel = sk.Kernel()
        chat_service = AzureChatCompletion(
            deployment_name=get_deployment_name(),
            endpoint=os.environ.get("AZURE_OPENAI_ENDPOINT"),
            api_key=os.environ.get("AZURE_OPENAI_API_KEY"),
        )
        kernel.add_service(chat_service)
        chat_history = ChatHistory()
        chat_history.add_system_message("You are a helpful AI assistant.")
        chat_history.add_user_message(user_message)
        settings = AzureChatPromptExecutionSettings(max_tokens=16384, temperature=0.7)
        result = await chat_service.get_chat_message_contents(chat_history, settings=settings)
        return str(result[0]) if result else "No response generated."
    except Exception as e:
        logger.error(f"Error calling Semantic Kernel: {e}")
        return f"Sorry, I encountered an error: {str(e)}"
