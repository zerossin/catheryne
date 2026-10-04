"""RPC dispatch and cross-platform state-directory lock."""
from pathlib import Path
import os

def compact_report(report):
    """A projection of the canonical report; the durable ledger stays complete."""
    if 'mode' not in report:
        return report
    fields = ('plan_id', 'stage', 'mode', 'owner', 'paused', 'pause_available',
              'urgent', 'dialogue_executor', 'stopped', 'alert', 'pending_result', 'observation_wall',
              'assistance', 'input_progress', 'external_task', 'cursor', 'input_enabled', 'progress', 'watchdog', 'control', 'settings', 'handoff_pending', 'host_pid',
              'plan_title', 'stage_title', 'session_seconds', 'supervisor_wait_seconds', 'supervisor_overdue')
    result = {key: report[key] for key in fields if key in report}
    result['plan_steps'] = report.get('plan_steps', [])
    result['steps'] = {key: {'status': value['status']} for key, value in report.get('steps', {}).items()}
    stage = report.get('stage')
    if stage is not None:
        record = report['steps'][stage]
        result['stage_status'] = {key: record[key] for key in ('status', 'attempts', 'failures', 'actor', 'no_progress_failures') if key in record}
    result['unverified_earlier_steps'] = report.get('unverified_earlier_steps', [])
    return result


class StoryService:
    def __init__(self, director, enable_input=False, focus_target=None):
        self.director, self.enable_input = director, enable_input
        self.shutdown = None
        self.focus_target = focus_target

    def call(self, method, params, *, view='full'):
        if view not in ('full', 'compact'):
            raise ValueError('view must be full or compact')
        if method == 'focus':
            if not self.enable_input or self.focus_target is None:
                raise ValueError('This host has no game input permission')
            if params:
                raise ValueError('Focus uses the configured target only')
            with self.director.lock:
                if self.director.state['owner'] is not None:
                    raise ValueError('Stop active input before changing focus')
                self.focus_target()
                return dict(focused=True)
        if method == 'shutdown':
            if self.shutdown is None:
                raise RuntimeError('Host shutdown is not configured')
            self.director.stop('host_shutdown_requested')
            self.shutdown()
            return dict(shutting_down=True, input_enabled=self.enable_input)
        methods = {'status': self.director.report, 'report': self.director.report,
                   'observe': self.director.observe, 'think': self.director.think,
                   'begin_external': self.director.begin_external, 'end_external': self.director.end_external,
                   'start_assist': self.director.start_assist, 'run': self.director.run, 'check_dialogue': self.director.check_dialogue, 'result': self.director.result,
                   'complete': self.director.complete, 'rollback': self.director.rollback,
                   'stop': self.director.stop, 'set_control': self.director.set_control,
                   'configure': self.director.configure, 'begin_direct': self.director.begin_direct,
                   'end_direct': self.director.end_direct,
                   'capture_started': self.director.capture_started,
                   'capture_finished': self.director.capture_finished}
        if method not in methods:
            raise ValueError('Unknown story command')
        if not self.enable_input and (method in ('run', 'start_assist', 'begin_direct', 'begin_external', 'check_dialogue') or params.get('pause_available')):
            raise ValueError('This host has no game input permission; restart with --enable-input')
        result = methods[method](**params)
        result['input_enabled'] = self.enable_input
        result['host_pid'] = os.getpid()
        return compact_report(result) if view == 'compact' else result


class HostLock:
    """OS-released lock prevents two story hosts owning the same local workspace."""
    def __init__(self, path):
        self.path = Path(path)

    def __enter__(self):
        import os
        self.path.parent.mkdir(parents=True, exist_ok=True)
        self.stream = self.path.open('a+b')
        self.stream.seek(0, 2)
        if not self.stream.tell():
            self.stream.write(b'0')
            self.stream.flush()
        self.stream.seek(0)
        try:
            if os.name == 'nt':
                import msvcrt
                msvcrt.locking(self.stream.fileno(), msvcrt.LK_NBLCK, 1)
            else:
                import fcntl
                fcntl.flock(self.stream.fileno(), fcntl.LOCK_EX | fcntl.LOCK_NB)
        except OSError:
            self.stream.close()
            raise RuntimeError('Another story host owns this workspace')
        return self

    def __exit__(self, *args):
        self.stream.close()
