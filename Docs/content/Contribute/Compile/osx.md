---
title: "macOS"
draft: false
---

APSIM can be compiled using Microsoft's [Visual Studio Code](https://code.visualstudio.com/download). A single solution file exists in the root of the repository (ApsimX.sln). Building this solution will extract all 3rd party packages from NuGet and build everything. All executables will be built to the bin folder, but the exact output location will depend on how the solution is built (ie release vs debug). The default (debug) will cause outputs to be copied to `bin/Debug/net8.0/`.

1. Install [Visual Studio Code](https://code.visualstudio.com/download)

2. Once visual studio is installed, install the vs code c# dev kit extension.

3. Install [git](https://git-scm.com/downloads) and a git client, we recommend [Fork](https://git-fork.com/).

4. Install the .NET 6 SDK. The SDK can be found [here](https://dotnet.microsoft.com/en-us/download/dotnet/6.0) for your specific operating system.

5. Check that the SDK is installed by opening a terminal and running the command: 

    ```
    dotnet --list-sdks
    ```
    - You should see at least one line that says:

        ```
        6.X.X
        ```
        - `x` is any number. The version numbers of a SDK may change over time.
        - There may be other lines with differing values. This is normal.


5. Install GTK+3 and gtksourceview4

    ```
    brew install gtk+3
    brew install gtksourceview4
    ```

6. Obtain the source code

    ```
    git clone https://github.com/APSIMInitiative/ApsimX
    ```

7. Build and Run