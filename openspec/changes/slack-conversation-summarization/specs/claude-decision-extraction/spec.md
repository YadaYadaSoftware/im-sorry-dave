## ADDED Requirements

### Requirement: Extract decisions, answers, and summaries

The system SHALL use Claude to analyze a conversation unit and produce structured candidates of three kinds: decisions made, answers to open questions, and a concise summary. Each candidate SHALL include the supporting evidence (referenced messages) and the contributing participants.

#### Scenario: Decision candidate produced

- **WHEN** extraction runs over a conversation in which the team agrees on a course of action
- **THEN** the system produces a decision candidate containing the decision statement, the referenced messages, and the participants involved

#### Scenario: Answer candidate linked to a question

- **WHEN** the conversation answers a previously recorded open question for the work item
- **THEN** the system produces an answer candidate linked to that question

#### Scenario: No actionable content

- **WHEN** the conversation contains no decisions or answers
- **THEN** the system produces no decision/answer candidates and may still produce a summary

### Requirement: Confidence and grounding

Each candidate SHALL carry a confidence indicator and SHALL be grounded in actual messages, so unsupported or hallucinated conclusions are not presented as fact.

#### Scenario: Low-confidence candidate flagged

- **WHEN** extraction is uncertain whether a conclusion was reached
- **THEN** the candidate is marked low-confidence so downstream confirmation treats it cautiously

#### Scenario: Candidate cites its evidence

- **WHEN** any candidate is produced
- **THEN** it references the specific messages that support it

### Requirement: Sensitive-content handling

The system SHALL apply the configured redaction/consent policy to extraction inputs and outputs before any content is forwarded for write-back.

#### Scenario: Redaction applied before write-back

- **WHEN** a candidate contains content matching the redaction policy
- **THEN** the system redacts or withholds it according to policy before it can be written to Jira
