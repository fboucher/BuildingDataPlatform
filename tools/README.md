# Tools Overview

Use this section to quickly find the tool that matches your workflow and use case.

The tools are small utility applications that help with common tasks: downloading reference data for semantic modeling, configuring equipment and sensors, and other hands-on activities when working with the EcoStruxure Building Data Platform.

Each tool is self-contained, includes setup and run guidance in its local README, and demonstrates practical implementation patterns.

## Available Tools

| Tool | Language | Purpose | What it helps you do |
| --- | --- | --- | --- |
| [Bricks & QUDT Reference Data Sync](brick-qudt-sync/) | .NET | Reference data synchronization | Download BrickSchema and QUDT ontology data locally for semantic modeling of equipment, points, and rooms |
| [Equipment Wizard TUI](equipment-wizard-tui/) | .NET | Interactive configuration | Generate equipment and sensor configuration JSON files using a terminal-based wizard |

## Tool Pattern

The tools follow a practical, task-focused pattern:

- Start from a concrete workflow task you need to perform.
- Use a tool built to streamline that specific task.
- Follow setup and run guidance in each tool's folder so details stay local to the implementation.
- Output files are ready to use with the platform APIs.

## Conventions Across Tools

- Each tool includes a `README.md` with quick-start guidance and prerequisites.
- Setup and environment configuration details are documented per tool in each local folder.
- Security notes are documented per tool where applicable.

## Related Guidance

- [Examples Overview](../examples/README.md) – Runnable examples for consumers and providers
- [Documentation Summary](../docs/README.md) – Full guides for setup, consuming data, and providing data
- [Setup Credentials](../docs/setup-credentials.md) – Secure retrieval and storage of API credentials
- [Providing Data](../docs/providing-data.md) – Data Integration Framework onboarding and configuration
