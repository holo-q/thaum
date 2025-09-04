import os
import logging
from typing import Dict, Any, Optional
from abc import ABC, abstractmethod


class IPromptLoader(ABC):
    @abstractmethod
    async def load_prompt_async(self, prompt_name: str) -> str:
        pass

    @abstractmethod
    async def format_prompt_async(self, prompt_name: str, parameters: Dict[str, Any]) -> str:
        pass


class PromptLoader(IPromptLoader):
    def __init__(self, logger: logging.Logger, prompts_directory: Optional[str] = None):
        self.logger = logger
        self.prompts_directory = prompts_directory or os.path.join(os.getcwd(), "prompts")
        self.prompt_cache: Dict[str, str] = {}

    async def load_prompt_async(self, prompt_name: str) -> str:
        if prompt_name in self.prompt_cache:
            return self.prompt_cache[prompt_name]

        prompt_path = os.path.join(self.prompts_directory, f"{prompt_name}.txt")

        if not os.path.exists(prompt_path):
            raise FileNotFoundError(f"Prompt file not found: {prompt_path}")

        try:
            with open(prompt_path, 'r', encoding='utf-8') as file:
                content = file.read()
            
            self.prompt_cache[prompt_name] = content
            self.logger.debug(f"Loaded prompt: {prompt_name} from {prompt_path}")
            return content
        except Exception as ex:
            self.logger.error(f"Failed to load prompt: {prompt_name} - {ex}")
            raise

    async def format_prompt_async(self, prompt_name: str, parameters: Dict[str, Any]) -> str:
        template = await self.load_prompt_async(prompt_name)
        result = template

        for key, value in parameters.items():
            placeholder = f"{{{key}}}"
            value_str = str(value) if value is not None else ""
            result = result.replace(placeholder, value_str)

        return result