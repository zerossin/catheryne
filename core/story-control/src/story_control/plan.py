"""Task data is separate from the control engine."""
from .director import nonempty

def new_plan(ident, title):
    return dict(id=nonempty(ident, 'plan id'), title=nonempty(title, 'title'), draft=True,
        scope='Define the requested task and exclusions', sources=[],
        resume_rule='Observe the current objective before selecting a step',
        steps=[dict(id='stage-1', title='Name an observable milestone',
            modes=['navigation','interaction'], tools=['computer_use','sequence'],
            completion='Define visible completion evidence', checkpoint='Verify persistence',
            failure_cost='Describe losses and recovery', sources=[])])
