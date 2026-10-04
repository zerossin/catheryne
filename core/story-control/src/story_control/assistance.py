"""Local combat worker adapter; the story director owns input and the journal."""
from .combat_runtime import monotonic_ms
from .combat_tools import CombatTools, hud_observation


class AssistActuator:
    def __init__(self, executor, verified_pause=False, heal_key=None):
        self.executor, self.verified_pause = executor, verified_pause
        self.heal_key = heal_key

    def execute(self, action, cancel):
        if cancel.is_set():
            return
        keys = {'attack': 'attack', 'e': 'e', 'q': 'q', 'approach': 'w'}
        if self.verified_pause:
            keys['request_verified_pause'] = 'b'
        if self.heal_key is not None:
            keys['use_verified_heal'] = self.heal_key
        if action not in keys:
            raise ValueError('No verified input mapping for ' + action)
        self.executor.act(keys=[keys[action]], seconds=.08)

    def cancel(self):
        self.executor.stop()


class AssistSession:
    """Director lifecycle adapter for the existing combat tool implementation."""
    def __init__(self, source, executor, *, context, slots, seconds=120,
                 verified_pause=False, allow_approach=False, require_target=True, use_food=False):
        self.tools = CombatTools(source, lambda: AssistActuator(
            executor, verified_pause, 'z' if use_food else None))
        self.options = dict(goal='local combat assistance', context=context, slots=slots,
            seconds=seconds, verified_pause=verified_pause, allow_approach=allow_approach,
            mode='tracked' if require_target else 'assist', use_food=use_food, preflight=True)

    def start(self):
        self.tools.start_encounter(**self.options)

    def stop(self):
        self.tools.stop()

    def report(self, cursor=0):
        return self.tools.report(cursor)

    def join(self):
        if self.tools.worker:
            self.tools.worker.join(None)


class LocalHudSource:
    def __init__(self, target_exe, profile, context):
        from .combat_hud import HudReader
        from .game_capture import WindowCapture
        self.reader = HudReader.load(profile)
        self.capture = WindowCapture(context, target_exe=target_exe)

    def __call__(self, cancel):
        if cancel.wait(.08):
            return None
        result = self.capture(cancel)
        if result is None:
            return None
        image, started, context = result
        if image.size != self.reader.size:
            raise ValueError('Game resolution differs from the calibrated HUD profile')
        snapshot, _ = self.reader.read(image, started)
        return hud_observation(self.capture.frames, started, context, snapshot)
