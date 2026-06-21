## MODIFIED Requirements

### Requirement: Connected API is visible

The application SHALL operate against the **currently selected API target** and SHALL display that
target's name and base address in the status bar, so the user knows which backend (and therefore
which Jira) their actions reach. The console does not itself choose fake vs. real — it reflects
whatever the selected target's API is configured for — but it does choose **which target** it
talks to.

#### Scenario: Active target shown

- **WHEN** the application starts or the operator switches the target
- **THEN** the status bar shows the active target's name and base address

#### Scenario: Backend determined by the API

- **WHEN** the user performs an action
- **THEN** the result reflects the selected target API's configured backend (fake or real Jira), not a mode chosen by the console
