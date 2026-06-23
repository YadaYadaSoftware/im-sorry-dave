## ADDED Requirements

### Requirement: Read-only conversational assistant over a work item

The system SHALL answer a user's natural-language question about a work item in Slack by calling
Claude with context grounded **only** in the mirrored `WorkItem` fields and the captured Slack
transcript for that item, and SHALL reply in the same channel (in-thread for a mention). The assistant
SHALL be strictly read-only: it SHALL NOT write to Jira and SHALL NOT invoke the decision write-back
path. The assistant SHALL be reachable both via a signature-verified `/ask <key> <question>` slash
command and via an @mention of the bot in a work item's channel.

#### Scenario: Slash command answers grounded in the work item and transcript

- **WHEN** a user runs `/ask <key> <question>` for a known work item and the Anthropic key is configured
- **THEN** the system loads the mirrored `WorkItem` and a bounded transcript window for that item
- **AND** calls Claude with only that context and the question
- **AND** posts the answer in the channel without writing anything to Jira

#### Scenario: Bot mention in a channel answers for that channel's work item

- **WHEN** a user @mentions the bot in a channel linked to a work item and asks a question
- **THEN** the system resolves the work item from the channel mapping
- **AND** answers grounded in that work item and its transcript, replying in the same thread

#### Scenario: Unknown or unlinked work item

- **WHEN** the slash command names a work item that does not exist, or the bot is mentioned in a
  channel with no linked work item
- **THEN** the system replies that it does not know that work item (or that the channel is not linked)
- **AND** does not call Claude

#### Scenario: Assistant unavailable when no Anthropic key is configured

- **WHEN** a user asks the assistant a question but no Anthropic API key is configured
- **THEN** the system replies that the assistant is unavailable
- **AND** does not fail the request

#### Scenario: Context handed to Claude is bounded

- **WHEN** the work item's channel has a long captured transcript
- **THEN** the system sends Claude a bounded window of the transcript (plus the compact work item
  fields) rather than the entire history, to cap token cost

#### Scenario: Assistant never writes to Jira

- **WHEN** any question is asked through the assistant
- **THEN** the system only reads the work item and transcript and posts a Slack reply
- **AND** makes no Jira write and does not trigger the decision write-back path
