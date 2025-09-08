from copy import deepcopy
from typing import Optional

from rich.table import Table
from rich.box import Box

from errloom.holoware import holoware_loader
from errloom.holoware.holophore import Holophore

from errloom.loom import Loom
from errloom.tapestry import Rollout

# logger = logging.getLogger(__name__)

# TODO allow multiple evaluation contexts
EMPTY: Box = Box(
    "    \n"
    "    \n"
    "    \n"
    "    \n"
    "    \n"
    "    \n"
    "    \n"
    "    \n"
)

class HolowareLoom(Loom):
    """
    Compression environment that generates compression/decompression pairs.

    For each prompt, generates:
    1. Compression rollout: original_content → compressed_form
    2. Decompression rollout: compressed_form → decompressed_content

    Both rollouts receive the same reward based on compression quality + fidelity.
    This works within standard GRPO framework without trainer modifications.
    """

    def __init__(
        self,
        path: str,
        model: Optional[str] = "Qwen/Qwen2.5-7B-Instruct",
        max_concurrent: int = 64,
        **kwargs
    ):
        super().__init__(
            model=model,
            max_concurrent=max_concurrent,
            message_type='completion',
            **kwargs
        )

        loader = holoware_loader.get_default_loader()
        # Store resolved holoware path for watcher/integration
        try:
            self.holoware_path = loader.find_holoware_path(path) or path
        except Exception:
            self.holoware_path = path
        self.holoware = loader.load_holoware(path)
        self._holoware_loader = loader  # keep for reload
        self._holoware_spec = path      # original spec for reload

        tb = Table(box=EMPTY)
        tb.add_column("Parameter", style="cyan", width=25)
        tb.add_column("Value", style="white")

        tb.add_row("Holoware path", str(getattr(self, "holoware_path", path)))
        # tb.add_row("Dataset size", str(len(self.data)))
        tb.add_row("Max concurrent", f"{max_concurrent}")
        tb.add_row("Model", model)

        self.logger.info(tb)
        self.logger.info(self.holoware.to_rich())

    def reload_holoware(self) -> None:
        """
        Re-parse and reload the holoware from disk. Safe to call between runs.
        """
        try:
            loader = getattr(self, "_holoware_loader", None) or holoware_loader.get_default_loader()
            spec = getattr(self, "_holoware_spec", None)
            if spec is None:
                return
            # Ask loader to drop any caches if it supports it
            try:
                if hasattr(loader, "clear_cache"):
                    loader.clear_cache()  # type: ignore[attr-defined]
            except Exception:
                pass
            # Re-resolve the path in case it changed
            try:
                self.holoware_path = loader.find_holoware_path(spec) or spec
            except Exception:
                self.holoware_path = spec
            # Reload the holoware callable
            self.holoware = loader.load_holoware(spec)
            self.logger.info(f"[green]✓[/] Reloaded holoware: {spec}")
        except Exception as e:
            self.logger.warning(f"[yellow]⚠ Failed to reload holoware: {e}[/]")

    def rollout(self, roll: Rollout):
        self.logger.push_debug("ROLLOUT")
        self.logger.info(f"Rollout ...")
        env = deepcopy(roll.row)
        phore = Holophore(self, roll, env)
        phore = self.holoware(phore)
        self.logger.pop()
        return roll

# def generate(self,
#              inputs: Dict[str, List[Any]] | Dataset | List[Any] | None = None,
#              client: OpenAI | None = None,
#              model: str | None = None,
#              sampling_args={},
#              max_concurrent: int | None = None,
#              score_rollouts: bool = True,
#              **kwargs: Any):
#     """
#     Overrides the base generate method to perform a full compression/decompression cycle.
#     """
#     # Support legacy signature where the first positional/keyword argument
#     # was named ``dataset``.
#     if max_concurrent is None:
#         max_concurrent = self.max_concurrent
#
#     # ------------------------------------------------------------------
#     # Normalise *inputs* to a flat list so we can iterate easily.
#     # ------------------------------------------------------------------
#
#     # Convert Dataset → list[dict]
#     # TODO this should be handled outside
#     # if inputs is None and 'dataset' in kwargs:
#     #     inputs = kwargs.pop('dataset')
#     # if isinstance(inputs, Dataset):
#     #     dataset_iter: List[Any] = list(inputs)
#     # elif isinstance(inputs, dict):
#     #     # Convert columnar dict → list of row dicts
#     #     keys = list(inputs.keys())
#     #     if not keys:
#     #         dataset_iter = []
#     #     else:
#     #         num_rows = len(inputs[keys[0]])
#     #         dataset_iter = [{k: inputs[k][i] for k in keys} for i in range(num_rows)]
#     # elif isinstance(inputs, list):
#     #     dataset_iter = inputs
#     # else:
#     #     raise TypeError(f"Unsupported inputs type: {type(inputs)}")
#
#     # Run the async main function
#     processed_results: List[EnvOutput] = self.run_processing(
#         items=dataset_iter,
#         client=client,
#         model=model,
#         sampling_args=sampling_args,
#         max_concurrent=max_concurrent,
#         **kwargs
#     )
#
#     processed_results = [r for r in processed_results if r is not None]
#
#     if not processed_results:
#         return EnvOutput(prompt=[], completion=[], reward=[], answer=[], state=[], extra={})
#
#     final_prompt, final_completion, final_reward, final_answer, final_state = [], [], [], [], []
#
#     final_extra = {}
#     extra_keys = None
#
#     for r in processed_results:
#         final_prompt.extend(r.prompt or [])
#         final_completion.extend(r.completion or [])
#         final_reward.extend(r.reward or [])
#         final_answer.extend(r.answer or [])
#         final_state.extend(r.state or [])
#
#         if r.extra:
#             if extra_keys is None:
#                 extra_keys = r.extra.keys()
#                 final_extra = {k: [] for k in extra_keys}
#
#             for key in extra_keys:
#                 if key in r.extra:
#                     final_extra[key].extend(r.extra[key] or [])
#
#     return EnvOutput(
#         prompt=final_prompt,
#         completion=final_completion,
#         reward=final_reward,
#         answer=final_answer,
#         state=final_state,
#         extra=final_extra,
#     )
