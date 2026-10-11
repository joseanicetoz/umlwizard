# Security Policy

## Reporting a vulnerability

Please do not report security issues in public issues. Report them privately through
[GitHub's private vulnerability reporting](https://github.com/joseanicetoz/umlwizard/security/advisories/new)
(the **Security** tab → **Report a vulnerability**), with the version, the operating system and the steps to
reproduce it.

You will get an answer within a week. Fixes are released in the next version, and the advisory is published with it.

## Supported versions

Only the latest release gets security fixes.

## Things to know

- **Plug-ins are code.** Every DLL in the `plugins` folder next to the executable or in the user's plug-in folder
  (`UmlWizard/plugins` under the application data folder) is loaded into the application and runs with its
  permissions. Install only plug-ins from sources you trust.
- **AI assistant.** When it is enabled, the active diagram and your messages are sent to the AI service set in
  File → Preferences. API keys are kept in the operating system's secret store, not in the settings file.
- **Project files** (`.umlwiz`) and imported OpenAPI, PlantUML and source files are read as data. A file that makes
  the application crash, hang or run code is a vulnerability: please report it.
