## ADDED Requirements

### Requirement: Slash commands are plugins behind a common contract

Every Slack slash command the system serves SHALL be implemented as a plugin behind a single common
contract. A plugin SHALL declare its command name, a human-readable description, the text used to
acknowledge an invocation, and a handler. No slash command may be served by code wired directly into
the HTTP endpoint.

The plugin is the unit of ownership for a **feature**, not merely a command: a plugin SHALL also own
the interactivity actions its command produces, so that a command and the buttons it posts are
enabled, disabled, and reasoned about together.

#### Scenario: A command is served through its plugin

- **WHEN** a slash command registered by a plugin is invoked
- **THEN** the system resolves the plugin from the registry and invokes its handler

#### Scenario: A plugin declares its manifest metadata

- **WHEN** the system inspects a registered plugin
- **THEN** the plugin supplies its command name and description without the system consulting any
  hardcoded per-command table

### Requirement: An administrator controls which commands are available

The system SHALL read the set of available slash commands from an administrator-controlled allow-list
in configuration. A command SHALL be registered only when its name appears in that allow-list. A
command whose name is absent SHALL NOT be registered, and absence SHALL be the default — the system
MUST NOT enable a command merely because a plugin for it exists.

Changing the allow-list SHALL be sufficient to enable or disable a command, without modifying,
recompiling against, or deleting the command's implementation.

#### Scenario: A command named in the allow-list is registered

- **WHEN** the system starts and a plugin's command name appears in the allow-list
- **THEN** that command is registered and served

#### Scenario: A command absent from the allow-list is not registered

- **WHEN** the system starts and a plugin's command name does not appear in the allow-list
- **THEN** that command is not registered, even though its implementation is present

#### Scenario: The allow-list is empty or unset

- **WHEN** the system starts with no allow-list configured
- **THEN** no slash commands are registered

#### Scenario: A disabled command is re-enabled

- **WHEN** an administrator adds a previously absent command name to the allow-list and the system
  restarts
- **THEN** that command is served again with its original behavior, with no code change

### Requirement: Inbound slash commands are dispatched by name

The system SHALL determine which command was invoked by reading the command name from the inbound
Slack request, and SHALL dispatch to the plugin that owns that name. The system MUST NOT assume the
identity of an inbound command.

#### Scenario: Dispatch to the owning plugin

- **WHEN** a signature-verified slash command request arrives naming a registered command
- **THEN** the system invokes that command's plugin and no other

#### Scenario: An unregistered command is refused

- **WHEN** a signature-verified slash command request arrives naming a command that is not registered,
  whether because it is disabled or because no plugin owns it
- **THEN** the system replies to the invoking user that the command is not available, and invokes no
  handler

#### Scenario: Signature verification precedes dispatch

- **WHEN** a slash command request fails Slack signature verification
- **THEN** the system rejects it as unauthorized without resolving or invoking any plugin

### Requirement: Interactivity actions are namespaced and owned by their plugin

Interactivity actions SHALL be identified by an action id namespaced to the owning plugin, so that two
plugins cannot claim the same action identifier. The system SHALL dispatch an inbound interactivity
payload to the plugin that owns the action's namespace.

An action belonging to a plugin whose command is not registered SHALL NOT be handled, so that
disabling a command also disables the buttons that command produces.

#### Scenario: An action routes to its owning plugin

- **WHEN** an interactivity payload arrives carrying a namespaced action id owned by a registered
  plugin
- **THEN** the system dispatches it to that plugin's handler for that action

#### Scenario: Two plugins declare the same bare action name

- **WHEN** two plugins each declare an action with the same unqualified name
- **THEN** their namespaced action ids differ and each resolves to its own plugin

#### Scenario: An action for a disabled command is refused

- **WHEN** an interactivity payload arrives for an action owned by a plugin whose command is not
  registered, such as a button on a message posted before the command was disabled
- **THEN** the system does not invoke the handler and reports that the action is no longer available

#### Scenario: An unrecognized action id is refused

- **WHEN** an interactivity payload arrives with an action id no registered plugin owns
- **THEN** the system does not invoke any handler

### Requirement: The host owns acknowledgement and background execution

The system SHALL acknowledge an inbound slash command within Slack's acknowledgement deadline using
the acknowledging text the plugin declares, and SHALL then run the plugin's handler outside the
request lifetime, delivering the handler's result to the user through the command's response URL.

Plugins SHALL NOT be responsible for meeting the acknowledgement deadline, for creating their own
execution scope, or for managing the lifetime of the inbound request. A handler that fails SHALL
report a failure to the invoking user rather than surfacing an unhandled error.

#### Scenario: Invocation is acknowledged promptly

- **WHEN** a registered command is invoked
- **THEN** the system acknowledges the invocation with the plugin's declared acknowledgement text
  before the handler completes

#### Scenario: A slow handler still delivers its result

- **WHEN** a plugin's handler takes longer to complete than the acknowledgement deadline allows
- **THEN** the handler runs to completion and its result is delivered to the invoking user via the
  response URL

#### Scenario: Handler execution outlives the request

- **WHEN** a plugin's handler runs after the inbound request has been acknowledged
- **THEN** the handler retains access to the services it requires for the duration of its work

#### Scenario: A failing handler is reported

- **WHEN** a plugin's handler throws
- **THEN** the system reports a failure to the invoking user and the error is logged

### Requirement: The Slack app manifest is generated from the enabled command set

The system SHALL generate the Slack app manifest's slash command declarations from the set of
registered commands, using each plugin's declared name and description. Configuration SHALL be the
source of truth and the manifest SHALL be a generated artifact; the system MUST NOT read the manifest
to determine which commands to serve.

The committed manifest SHALL be verifiable against the registry so that the two cannot silently drift.

#### Scenario: Generated manifest lists exactly the registered commands

- **WHEN** the manifest is generated
- **THEN** it declares every registered command and no command that is not registered

#### Scenario: A disabled command is absent from the manifest

- **WHEN** a command is removed from the allow-list and the manifest is regenerated
- **THEN** the command no longer appears in the manifest, so Slack no longer offers it to users

#### Scenario: Drift between the committed manifest and the registry is detected

- **WHEN** the committed manifest does not match what the registry would generate
- **THEN** the mismatch is reported as a failure

#### Scenario: Manifest is never treated as configuration

- **WHEN** the manifest declares a command that the allow-list does not enable
- **THEN** the system still does not serve that command

### Requirement: Moving a command behind the registry preserves its behavior

Relocating an existing command's implementation behind the plugin contract SHALL NOT change the
command's observable behavior. When a relocated command is enabled, it SHALL produce the same
outcomes, messages, and side effects as before it was relocated.

#### Scenario: A relocated command behaves identically when enabled

- **WHEN** a command whose implementation has been moved behind the plugin contract is enabled and
  invoked
- **THEN** it produces the same results it produced before the move

#### Scenario: Relocation does not degrade or stub the implementation

- **WHEN** a command's implementation is relocated behind the plugin contract
- **THEN** its logic is preserved rather than removed, disabled internally, or replaced with a
  placeholder
