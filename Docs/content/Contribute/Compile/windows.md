---
title: "Windows"
draft: false
---

APSIM can be compiled using Microsoft Visual Studio 2019 or later. A single solution file exists in the root of the repository (ApsimX.sln). All executables will be built to the bin directory. The exact output path will depend upon whether the solution is built in debug or release mode. The default (debug) will output files to `bin\Debug\net8.0\`. APSIM currently can only be built against .NET 6.

Building APSIM requires that version 6.0 of the .NET SDK is installed. This can be installed at the same time as Visual Studio. If Visual Studio is already installed, the installation can be modified by navigating to 'Tools\Get Tools and Features...' in the menu bar and modifying the existing installation by adding either the ".NET desktop development" or "Universal Windows Platform development" workload.

![Install the .NET Core SDK](/images/vs-modify-workload.png)

1. Open ApsimX.sln in visual studio
2. Build solution (default ctrl + shift + b)

    Right-click on the solution in the solution explorer and click "Build solution"