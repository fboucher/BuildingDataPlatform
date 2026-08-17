# Equipment Wizard TUI

![.NET](https://img.shields.io/badge/10.0-512BD4?logo=dotnet&logoColor=fff)

A terminal-based wizard that help you generating quickly and simply an equipment configuration JSON files using the [EcoStruxure Building Data Platform](https://ecostruxure-building-platform-uat.se.app/) (BDP) APIs. This JSON file can then be use in upload sensors information as a Data Provider. You can find and example of [Data Integration Framework (DIF) Telemetry Ingress](https://github.com/SchneiderElectricBuildings/BuildingDataPlatform#for-data-providers)


## Prerequisites

- .NET 10.0
- EcoStruxure Building Data Platform API credentials

## Quick Start

1. Clone the repository
2. Move to the `src` folder and copy `.env.example` to `.env` and add your credentials:
   ```bash
   cd src
   cp .env.example .env
   ```

   Set these environment variables or add to `.env`:
   - `API_BASE_URL` - API endpoint
   - `TENANT_ID` - Azure AD Tenant ID
   - `CLIENT_ID` - Azure AD Client ID
   - `CLIENT_SECRET` - Azure AD Client Secret
   - `CREDS_SCOPES` - OAuth2 scope

   See [Setup Credentials](https://github.com/SchneiderElectricBuildings/BuildingDataPlatform/blob/main/docs/setup-credentials.md) for more information.

3. Build and run:
   ```bash
   dotnet build
   dotnet run
   ```

![Equipment Wizard TUI in action](assets/demo.gif)

## Output

Generates a JSON configuration file with equipment and sensor definitions following the EcoStruxure Building Data Platform schema.
