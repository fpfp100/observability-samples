import logging
from agents import Agent, Runner
from microsoft_agents.hosting.core import TurnContext

logger = logging.getLogger(__name__)


async def call_openai_agents(user_message: str, context: TurnContext) -> str:
    try:
        agent = Agent(
            name="Assistant",
            instructions="You are a helpful AI assistant.",
            model="gpt-4o",
        )
        result = await Runner.run(agent, user_message)
        return result.final_output
    except Exception as e:
        logger.error(f"Error calling OpenAI Agents: {e}")
        return f"Sorry, I encountered an error: {str(e)}"
