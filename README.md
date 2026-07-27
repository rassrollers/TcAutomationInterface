# TwinCAT Automation Interface

Implementation of the TwinCAT Automation Interface core as a library.

Can be used for e.g. build server to interact with TwinCAT IDE or automated deployment.

This works for TwinCAT 3.1.4024 and 3.1.4026.

## Project Structure

The solution is organized into two projects:

| Project | Description |
|---------|-------------|
| `AutomationInterface.core` | Core library containing all TwinCAT Automation Interface logic, Git integration, VS environment management, and utility classes. |
| `AutomationInterface.testConsole` | Test project — Console application used during development and testing to parse CLI arguments and orchestrate the build/library workflow. Not part of the distributed library. |

## Architecture

The architecture of the project is structured as follows:

AutomationInterface.core:
* Environment/
    * TcXaeShellEnvironment       - High-level orchestrator for TwinCAT operations
    * VisualStudioEnvironment     - COM-based DTE wrapper with retry logic
    * DteHelper, IOleMessageFilter, MessageFilter, StaComHost, ErrorItemsEnumerable - COM and STA threading helpers for DTE access
* TwinCAT/
    * AutomationInterface (partial class)
        * AI_Base                 - System manager, configuration, and runtime control
        * AI_LibraryRepo          - Library reference management and installation
        * AI_Motion               - NC/Motion axis reload via Solution Explorer
        * AI_PlcProject           - PLC project references, version injection, library export
        * AI_ProgramItems         - Program item management (POU, DUT, GVL, etc.)
        * AI_System               - System setup (License, Task, etc.)
        * AI_Templates            - Templates for solution and projects
    * TcProjectXml                - .tsproj XML manipulation (version, variants, licenses)
    * PlcProjectXml               - .plcproj XML manipulation (version, release state, TcUnit)
    * TcUnitRunner                - TcUnit test result polling and reporting
    * BuildOptions                - CLI argument definitions
    * AI_Settings                 - AutomationInterfaceSettings, TreeItems, and enum definitions
* ADS/
    * AdsHandler                  - ADS connection and TwinCAT runtime state control
* Git/
    * IGitInfo                    - Interface for Git version data
    * GitInfo                     - LibGit2Sharp-based Git version extraction
* Exceptions/
    * TwinCatException.cs         - TwinCatException, AutomationInterfaceException, TcXmlException
    * WrongOSException.cs         - Non-Windows OS detection
* AppTools                        - Configuration, logging, and OS utilities
* FilesAndPathTools               - File and directory search helpers

## Requirements

- **OS:** Windows (required for TwinCAT COM interop)
- **.NET:** 8.0
- **TwinCAT XAE Shell 4024 or 4026:** Installed with matching DTE version

## Usage

Use the `Example.cs` and `appsettings.json` in the `Example` directory as a template for building a TwinCAT project and deploy it to a target.

### Command-Line Options

| Option | Short | Required | Description |
|--------|-------|----------|-------------|
| `--SolutionName` | `-s` | No | Solution filename to locate in the working directory |
| `--WorkDir` | `-w` | Yes | Root directory containing the `.git` folder |
| `--Type` | `-t` | No | Build type: `Library` or `Project` |
| `--UiXae` | `-u` | No | Show the TcXaeShell UI (default: `false`) |
| `--UserControl` | `-c` | No | Enable user control of the UI (default: `false`) |

## Configuration

Application settings are defined in `appsettings.json`:

- **Logging** — Standard .NET logging configuration
- **Licenses** — TwinCAT license keys to inject into the project
- **Target:NetId** — AMS Net ID of the target PLC
- **Target:Platform** — Target platform (e.g. `TwinCAT RT (x64)`)
- **UnitTest:TcUnitResultPath** — File path for TcUnit XML results

## Using as a Git Submodule

This repository can used as a Git submodule to other projects, allowing you to reference `AutomationInterface.core` directly.

> **Note:** Only the `AutomationInterface.core` project is intended for use as a library. The `AutomationInterface.testConsole` project is a test harness and should not be referenced by consuming projects.

### Adding the submodule

From the root of your consuming repository, run:

```
git submodule add https://github.com/rassrollers/TcAutomationInterface.git <Optional path>
```

If the repository was already cloned without the flag, initialize the submodule manually:

```
git submodule init
git submodule update
```

### Referencing the core project

Add a project reference from your `.csproj` to the `AutomationInterface.core` library:

Or via the Visual Studio UI: right-click your project -> **Add** -> **Project Reference** -> **Browse** -> select `libs\tcautomationinterface\AutomationInterface.core\AutomationInterface.core.csproj`.
