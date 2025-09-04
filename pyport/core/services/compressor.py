import logging

class Compressor:
    def __init__(self, llm, language_server, cache, prompt_loader, logger: logging.Logger):
        self.llm = llm
        self.language_server = language_server
        self.cache = cache
        self.prompt_loader = prompt_loader
        self.logger = logger
        self.logger.warning("Compressor is not yet implemented.")
