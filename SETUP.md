# LegionDeck Setup Instructions

The automated setup could not complete because the .NET 8 SDK is missing from the environment.

## Prerequisites
1.  **Install .NET 8 SDK**: Download and install from [https://dotnet.microsoft.com/download/dotnet/8.0](https://dotnet.microsoft.com/download/dotnet/8.0).

## Build and Run
Once the SDK is installed, open a terminal in this directory (`Legion_Launch`) and run:

1.  **Create Solution (if not exists)**:
    ```bash
    dotnet new sln -n LegionDeck
    dotnet sln add LegionDeck.CLI/LegionDeck.CLI.csproj
    ```

2.  **Restore Dependencies**:
    ```bash
    dotnet restore
    ```

3.  **Run the Steam Auth Command**:
    ```bash
    cd LegionDeck.CLI
    dotnet run -- auth --service steam
    ```

## Notes
- The application uses `WebView2` which requires a Windows UI message loop. The project is configured to use Windows Forms (`<UseWindowsForms>true</UseWindowsForms>`) to support this.
- When you run the steam auth command, a window should appear loading the Steam login page.
- Log in securely. The application waits for the `steamLoginSecure` cookie.
- Once detected, the window will close and the console will report success.
