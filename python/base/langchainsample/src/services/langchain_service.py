import logging
import os
from langchain_openai import AzureChatOpenAI
from langchain_core.messages import HumanMessage, SystemMessage
from microsoft_agents.hosting.core import TurnContext

logger = logging.getLogger(__name__)

async def call_langchain(user_message: str, context: TurnContext) -> str:
    try:
        llm = AzureChatOpenAI(
            azure_deployment=os.environ.get("AZURE_OPENAI_DEPLOYMENT", "gpt-4o"),
            azure_endpoint=os.environ.get("AZURE_OPENAI_ENDPOINT"),
            api_key=os.environ.get("AZURE_OPENAI_API_KEY"),
            api_version="2025-01-01-preview",
        )
        messages = [
            SystemMessage(content="You are a helpful AI assistant."),
            HumanMessage(content=user_message),
        ]
        response = await llm.ainvoke(messages)
        return response.content
    except Exception as e:
        logger.error(f"Error calling LangChain: {e}")
        return f"Sorry, I encountered an error: {str(e)}"
