import json
import logging
import os
from typing import Optional, List, Dict, Any, AsyncGenerator
from dataclasses import dataclass, field
from abc import ABC, abstractmethod
import aiohttp
import asyncio


@dataclass
class LlmOptions:
    temperature: float = 0.7
    max_tokens: int = 4096
    model: Optional[str] = None
    stop_sequences: Optional[List[str]] = None


@dataclass
class OpenAIMessage:
    role: str
    content: str


@dataclass
class OpenAIRequest:
    model: str
    messages: List[OpenAIMessage]
    temperature: float
    max_tokens: int
    stop: Optional[List[str]] = None


@dataclass
class OpenAIStreamRequest:
    model: str
    messages: List[OpenAIMessage]
    temperature: float
    max_tokens: int
    stream: bool
    stop: Optional[List[str]] = None


@dataclass
class OpenAIStreamDelta:
    content: Optional[str] = None


@dataclass
class OpenAIStreamChoice:
    delta: Optional[OpenAIStreamDelta] = None


@dataclass
class OpenAIChoice:
    message: Optional[OpenAIMessage] = None


@dataclass
class OpenAIResponse:
    choices: Optional[List[OpenAIChoice]] = None


@dataclass
class OpenAIStreamChunk:
    choices: Optional[List[OpenAIStreamChoice]] = None


@dataclass
class AnthropicMessage:
    role: str
    content: str


@dataclass
class AnthropicRequest:
    model: str
    messages: List[AnthropicMessage]
    temperature: float
    max_tokens: int
    system: Optional[str] = None


@dataclass
class AnthropicContent:
    text: Optional[str] = None


@dataclass
class AnthropicResponse:
    content: Optional[List[AnthropicContent]] = None


@dataclass
class OllamaOptions:
    temperature: float
    num_predict: int


@dataclass
class OllamaRequest:
    model: str
    prompt: str
    stream: bool
    options: Optional[OllamaOptions] = None


@dataclass
class OllamaResponse:
    response: Optional[str] = None


@dataclass
class OllamaStreamChunk:
    response: Optional[str] = None
    done: bool = False


class ILLM(ABC):
    @abstractmethod
    async def complete_async(self, prompt: str, options: Optional[LlmOptions] = None) -> str:
        pass

    @abstractmethod
    async def complete_with_system_async(self, system_prompt: str, user_prompt: str, options: Optional[LlmOptions] = None) -> str:
        pass

    @abstractmethod
    async def stream_complete_async(self, prompt: str, options: Optional[LlmOptions] = None) -> AsyncGenerator[str, None]:
        pass


class HttpLLM(ILLM):
    def __init__(self, configuration: Dict[str, Any], logger: logging.Logger):
        self.configuration = configuration
        self.logger = logger

    async def complete_async(self, prompt: str, options: Optional[LlmOptions] = None) -> str:
        if options is None:
            options = LlmOptions()

        provider = self.configuration.get("llm", {}).get("provider")
        if not provider:
            raise ValueError("LLM:Provider configuration is required")

        provider = provider.lower()
        if provider == "openai":
            return await self._complete_openai_async(prompt, options)
        elif provider == "anthropic":
            return await self._complete_anthropic_async(prompt, options)
        elif provider == "ollama":
            return await self._complete_ollama_async(prompt, options)
        elif provider == "openrouter":
            return await self._complete_openrouter_async(prompt, options)
        else:
            raise ValueError(f"Provider {provider} not supported")

    async def complete_with_system_async(self, system_prompt: str, user_prompt: str, options: Optional[LlmOptions] = None) -> str:
        if options is None:
            options = LlmOptions()

        provider = self.configuration.get("llm", {}).get("provider")
        if not provider:
            raise ValueError("LLM:Provider configuration is required")

        provider = provider.lower()
        if provider == "openai":
            return await self._complete_openai_with_system_async(system_prompt, user_prompt, options)
        elif provider == "anthropic":
            return await self._complete_anthropic_with_system_async(system_prompt, user_prompt, options)
        elif provider == "ollama":
            return await self._complete_ollama_with_system_async(system_prompt, user_prompt, options)
        elif provider == "openrouter":
            return await self._complete_openrouter_with_system_async(system_prompt, user_prompt, options)
        else:
            raise ValueError(f"Provider {provider} not supported")

    async def stream_complete_async(self, prompt: str, options: Optional[LlmOptions] = None) -> AsyncGenerator[str, None]:
        if options is None:
            options = LlmOptions()

        provider = self.configuration.get("llm", {}).get("provider")
        if not provider:
            raise ValueError("LLM:Provider configuration is required")

        provider = provider.lower()
        if provider == "openai":
            async for token in self._stream_openai_async(prompt, options):
                yield token
        elif provider == "anthropic":
            async for token in self._stream_anthropic_async(prompt, options):
                yield token
        elif provider == "ollama":
            async for token in self._stream_ollama_async(prompt, options):
                yield token
        elif provider == "openrouter":
            async for token in self._stream_openrouter_async(prompt, options):
                yield token
        else:
            raise ValueError(f"Provider {provider} streaming not supported")

    async def _complete_openai_async(self, prompt: str, options: LlmOptions) -> str:
        request = OpenAIRequest(
            model=options.model or "gpt-4",
            messages=[OpenAIMessage(role="user", content=prompt)],
            temperature=options.temperature,
            max_tokens=options.max_tokens,
            stop=options.stop_sequences
        )
        return await self._send_openai_request_async(request)

    async def _complete_openai_with_system_async(self, system_prompt: str, user_prompt: str, options: LlmOptions) -> str:
        request = OpenAIRequest(
            model=options.model or "gpt-4",
            messages=[
                OpenAIMessage(role="system", content=system_prompt),
                OpenAIMessage(role="user", content=user_prompt)
            ],
            temperature=options.temperature,
            max_tokens=options.max_tokens,
            stop=options.stop_sequences
        )
        return await self._send_openai_request_async(request)

    async def _send_openai_request_async(self, request: OpenAIRequest) -> str:
        base_url = self.configuration.get("llm", {}).get("base_url")
        if not base_url:
            raise ValueError("LLM:BaseUrl configuration is required for OpenAI provider")

        request_dict = {
            "model": request.model,
            "messages": [{"role": msg.role, "content": msg.content} for msg in request.messages],
            "temperature": request.temperature,
            "max_tokens": request.max_tokens
        }
        if request.stop:
            request_dict["stop"] = request.stop

        api_key = os.getenv("OPENAI_API_KEY") or self.configuration.get("llm", {}).get("api_key")
        headers = {
            "Content-Type": "application/json"
        }
        if api_key:
            headers["Authorization"] = f"Bearer {api_key}"

        try:
            async with aiohttp.ClientSession() as session:
                async with session.post(f"{base_url}/chat/completions", json=request_dict, headers=headers) as response:
                    response.raise_for_status()
                    response_json = await response.json()
                    
                    choices = response_json.get("choices", [])
                    if choices:
                        return choices[0].get("message", {}).get("content", "")
                    return ""
        except Exception as ex:
            self.logger.error(f"Failed to complete OpenAI request: {ex}")
            raise

    async def _complete_anthropic_async(self, prompt: str, options: LlmOptions) -> str:
        request = AnthropicRequest(
            model=options.model.replace("gpt-4", "claude-3-sonnet-20240229") if options.model else "claude-3-sonnet-20240229",
            messages=[AnthropicMessage(role="user", content=prompt)],
            temperature=options.temperature,
            max_tokens=options.max_tokens
        )

        base_url = self.configuration.get("llm", {}).get("base_url")
        if not base_url:
            raise ValueError("LLM:BaseUrl configuration is required for Anthropic provider")

        request_dict = {
            "model": request.model,
            "messages": [{"role": msg.role, "content": msg.content} for msg in request.messages],
            "temperature": request.temperature,
            "max_tokens": request.max_tokens
        }

        api_key = os.getenv("ANTHROPIC_API_KEY") or self.configuration.get("llm", {}).get("api_key")
        headers = {
            "Content-Type": "application/json"
        }
        if api_key:
            headers["x-api-key"] = api_key
            headers["anthropic-version"] = "2023-06-01"

        try:
            async with aiohttp.ClientSession() as session:
                async with session.post(f"{base_url}/messages", json=request_dict, headers=headers) as response:
                    response.raise_for_status()
                    response_json = await response.json()
                    
                    content = response_json.get("content", [])
                    if content:
                        return content[0].get("text", "")
                    return ""
        except Exception as ex:
            self.logger.error(f"Failed to complete Anthropic request: {ex}")
            raise

    async def _complete_anthropic_with_system_async(self, system_prompt: str, user_prompt: str, options: LlmOptions) -> str:
        request = AnthropicRequest(
            model=options.model.replace("gpt-4", "claude-3-sonnet-20240229") if options.model else "claude-3-sonnet-20240229",
            system=system_prompt,
            messages=[AnthropicMessage(role="user", content=user_prompt)],
            temperature=options.temperature,
            max_tokens=options.max_tokens
        )

        base_url = self.configuration.get("llm", {}).get("base_url")
        if not base_url:
            raise ValueError("LLM:BaseUrl configuration is required for Anthropic provider")

        request_dict = {
            "model": request.model,
            "system": request.system,
            "messages": [{"role": msg.role, "content": msg.content} for msg in request.messages],
            "temperature": request.temperature,
            "max_tokens": request.max_tokens
        }

        api_key = os.getenv("ANTHROPIC_API_KEY") or self.configuration.get("llm", {}).get("api_key")
        headers = {
            "Content-Type": "application/json"
        }
        if api_key:
            headers["x-api-key"] = api_key
            headers["anthropic-version"] = "2023-06-01"

        try:
            async with aiohttp.ClientSession() as session:
                async with session.post(f"{base_url}/messages", json=request_dict, headers=headers) as response:
                    response.raise_for_status()
                    response_json = await response.json()
                    
                    content = response_json.get("content", [])
                    if content:
                        return content[0].get("text", "")
                    return ""
        except Exception as ex:
            self.logger.error(f"Failed to complete Anthropic request: {ex}")
            raise

    async def _complete_ollama_async(self, prompt: str, options: LlmOptions) -> str:
        request = OllamaRequest(
            model=options.model or "llama2",
            prompt=prompt,
            stream=False,
            options=OllamaOptions(
                temperature=options.temperature,
                num_predict=options.max_tokens
            )
        )

        base_url = self.configuration.get("llm", {}).get("base_url")
        if not base_url:
            raise ValueError("LLM:BaseUrl configuration is required for Ollama provider")

        request_dict = {
            "model": request.model,
            "prompt": request.prompt,
            "stream": request.stream,
            "options": {
                "temperature": request.options.temperature,
                "num_predict": request.options.num_predict
            }
        }

        try:
            async with aiohttp.ClientSession() as session:
                async with session.post(f"{base_url}/api/generate", json=request_dict) as response:
                    response.raise_for_status()
                    response_json = await response.json()
                    return response_json.get("response", "")
        except Exception as ex:
            self.logger.error(f"Failed to complete Ollama request: {ex}")
            raise

    async def _complete_ollama_with_system_async(self, system_prompt: str, user_prompt: str, options: LlmOptions) -> str:
        full_prompt = f"System: {system_prompt}\n\nUser: {user_prompt}"
        return await self._complete_ollama_async(full_prompt, options)

    async def _complete_openrouter_async(self, prompt: str, options: LlmOptions) -> str:
        request = OpenAIRequest(
            model=options.model or "openai/gpt-4",
            messages=[OpenAIMessage(role="user", content=prompt)],
            temperature=options.temperature,
            max_tokens=options.max_tokens,
            stop=options.stop_sequences
        )
        return await self._send_openrouter_request_async(request)

    async def _complete_openrouter_with_system_async(self, system_prompt: str, user_prompt: str, options: LlmOptions) -> str:
        request = OpenAIRequest(
            model=options.model or "openai/gpt-4",
            messages=[
                OpenAIMessage(role="system", content=system_prompt),
                OpenAIMessage(role="user", content=user_prompt)
            ],
            temperature=options.temperature,
            max_tokens=options.max_tokens,
            stop=options.stop_sequences
        )
        return await self._send_openrouter_request_async(request)

    async def _send_openrouter_request_async(self, request: OpenAIRequest) -> str:
        base_url = self.configuration.get("llm", {}).get("base_url")
        if not base_url:
            raise ValueError("LLM:BaseUrl configuration is required for OpenRouter provider")

        request_dict = {
            "model": request.model,
            "messages": [{"role": msg.role, "content": msg.content} for msg in request.messages],
            "temperature": request.temperature,
            "max_tokens": request.max_tokens
        }
        if request.stop:
            request_dict["stop"] = request.stop

        api_key = os.getenv("OPENROUTER_API_KEY") or self.configuration.get("llm", {}).get("api_key")
        app_name = self.configuration.get("llm", {}).get("app_name", "Thaum")
        site_url = self.configuration.get("llm", {}).get("site_url", "https://github.com/your-repo/thaum")

        headers = {
            "Content-Type": "application/json",
            "HTTP-Referer": site_url,
            "X-Title": app_name
        }
        if api_key:
            headers["Authorization"] = f"Bearer {api_key}"

        try:
            async with aiohttp.ClientSession() as session:
                async with session.post(f"{base_url}/chat/completions", json=request_dict, headers=headers) as response:
                    if not response.ok:
                        error_content = await response.text()
                        self.logger.error(f"OpenRouter API error {response.status}: {error_content}")
                        raise aiohttp.ClientError(f"OpenRouter API error {response.status}: {error_content}")

                    response_json = await response.json()
                    choices = response_json.get("choices", [])
                    if choices:
                        return choices[0].get("message", {}).get("content", "")
                    return ""
        except Exception as ex:
            self.logger.error(f"Failed to complete OpenRouter request: {ex}")
            raise

    async def _stream_openai_async(self, prompt: str, options: LlmOptions) -> AsyncGenerator[str, None]:
        request = OpenAIStreamRequest(
            model=options.model or "gpt-4",
            messages=[OpenAIMessage(role="user", content=prompt)],
            temperature=options.temperature,
            max_tokens=options.max_tokens,
            stream=True,
            stop=options.stop_sequences
        )

        base_url = self.configuration.get("llm", {}).get("base_url")
        if not base_url:
            raise ValueError("LLM:BaseUrl configuration is required for OpenAI provider")

        request_dict = {
            "model": request.model,
            "messages": [{"role": msg.role, "content": msg.content} for msg in request.messages],
            "temperature": request.temperature,
            "max_tokens": request.max_tokens,
            "stream": request.stream
        }
        if request.stop:
            request_dict["stop"] = request.stop

        api_key = os.getenv("OPENAI_API_KEY") or self.configuration.get("llm", {}).get("api_key")
        headers = {
            "Content-Type": "application/json"
        }
        if api_key:
            headers["Authorization"] = f"Bearer {api_key}"

        async with aiohttp.ClientSession() as session:
            async with session.post(f"{base_url}/chat/completions", json=request_dict, headers=headers) as response:
                response.raise_for_status()
                
                async for line in response.content:
                    line = line.decode().strip()
                    if line.startswith("data: "):
                        data = line[6:]  # Remove "data: " prefix
                        if data == "[DONE]":
                            break

                        try:
                            chunk_data = json.loads(data)
                            choices = chunk_data.get("choices", [])
                            if choices:
                                delta = choices[0].get("delta", {})
                                content = delta.get("content")
                                if content:
                                    yield content
                        except json.JSONDecodeError:
                            # Skip invalid JSON lines
                            continue

    async def _stream_anthropic_async(self, prompt: str, options: LlmOptions) -> AsyncGenerator[str, None]:
        # Anthropic streaming - fallback to batch for now
        result = await self._complete_anthropic_async(prompt, options)
        yield result

    async def _stream_ollama_async(self, prompt: str, options: LlmOptions) -> AsyncGenerator[str, None]:
        request = OllamaRequest(
            model=options.model or "llama2",
            prompt=prompt,
            stream=True,
            options=OllamaOptions(
                temperature=options.temperature,
                num_predict=options.max_tokens
            )
        )

        base_url = self.configuration.get("llm", {}).get("base_url")
        if not base_url:
            raise ValueError("LLM:BaseUrl configuration is required for Ollama provider")

        request_dict = {
            "model": request.model,
            "prompt": request.prompt,
            "stream": request.stream,
            "options": {
                "temperature": request.options.temperature,
                "num_predict": request.options.num_predict
            }
        }

        async with aiohttp.ClientSession() as session:
            async with session.post(f"{base_url}/api/generate", json=request_dict) as response:
                response.raise_for_status()
                
                async for line in response.content:
                    line = line.decode().strip()
                    if line:
                        try:
                            chunk_data = json.loads(line)
                            response_text = chunk_data.get("response")
                            if response_text:
                                yield response_text
                            if chunk_data.get("done"):
                                break
                        except json.JSONDecodeError:
                            # Skip invalid JSON lines
                            continue

    async def _stream_openrouter_async(self, prompt: str, options: LlmOptions) -> AsyncGenerator[str, None]:
        request = OpenAIStreamRequest(
            model=options.model or "openai/gpt-4",
            messages=[OpenAIMessage(role="user", content=prompt)],
            temperature=options.temperature,
            max_tokens=options.max_tokens,
            stream=True,
            stop=options.stop_sequences
        )

        base_url = self.configuration.get("llm", {}).get("base_url")
        if not base_url:
            raise ValueError("LLM:BaseUrl configuration is required for OpenRouter provider")

        request_dict = {
            "model": request.model,
            "messages": [{"role": msg.role, "content": msg.content} for msg in request.messages],
            "temperature": request.temperature,
            "max_tokens": request.max_tokens,
            "stream": request.stream
        }
        if request.stop:
            request_dict["stop"] = request.stop

        api_key = os.getenv("OPENROUTER_API_KEY") or self.configuration.get("llm", {}).get("api_key")
        app_name = self.configuration.get("llm", {}).get("app_name", "Thaum")
        site_url = self.configuration.get("llm", {}).get("site_url", "https://github.com/your-repo/thaum")

        headers = {
            "Content-Type": "application/json",
            "HTTP-Referer": site_url,
            "X-Title": app_name
        }
        if api_key:
            headers["Authorization"] = f"Bearer {api_key}"

        async with aiohttp.ClientSession() as session:
            async with session.post(f"{base_url}/chat/completions", json=request_dict, headers=headers) as response:
                if not response.ok:
                    error_content = await response.text()
                    self.logger.error(f"OpenRouter streaming API error {response.status}: {error_content}")
                    raise aiohttp.ClientError(f"OpenRouter streaming API error {response.status}: {error_content}")
                
                async for line in response.content:
                    line = line.decode().strip()
                    if line.startswith("data: "):
                        data = line[6:]  # Remove "data: " prefix
                        if data == "[DONE]":
                            break

                        try:
                            chunk_data = json.loads(data)
                            choices = chunk_data.get("choices", [])
                            if choices:
                                delta = choices[0].get("delta", {})
                                content = delta.get("content")
                                if content:
                                    yield content
                        except json.JSONDecodeError:
                            # Skip invalid JSON lines
                            continue