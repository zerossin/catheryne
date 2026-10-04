# Local security boundaries

Do not attach connection files, tokens, journals, screenshots or account exports to public reports.
The host binds only to loopback and requires a random bearer token. Use a private state directory.
Local processes with access to that directory can act as clients; this is not a multi-user sandbox.

Input is opt-in and foreground-bound. The service does not elevate itself, change security settings,
or launch games. Send a minimal synthetic reproduction when reporting defects. A private reporting
channel must be configured by the repository owner before public distribution; none is invented here.
