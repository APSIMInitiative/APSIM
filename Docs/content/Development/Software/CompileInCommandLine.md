---
title: 'Publich APSIMNG from source codes'
date: '2023-03-07'
slug: compile-apsimng
---


## Prerequisite

* git
* Visual Studio 2022


## Checkout the latest source codes from APSIMInitiative

```
git clone  --depth 1  https://github.com/APSIMInitiative/ApsimX.git
```



## Publish solution for batch mode

Run following command under root folders on Windows

Publish for windows

```
dotnet publish -c Release -f net8.0 -r win-x64   --self-contained ApsimX.sln
```

Publish for Ubuntu

```
dotnet publish -c Release -f net8.0 -r ubuntu.20.04-x64 --self-contained ApsimX.sln
```

Publish for SLES (e.g. CSIRO cluster):

```
dotnet publish -c Release -f net8.0 -r sles.15-x64 --self-contained ApsimX.sln
```

All runtime identify for other operating system can be found from [github](https://github.com/dotnet/runtime/blob/main/src/libraries/Microsoft.NETCore.Platforms/src/runtime.json)

Copy the contents `bin/Release/net8.0/<runtime-identify>/publish/` to your operating system.

## .NET runtime
Apsim NG has been updated to run using .NET 6.0. This can be downloaded [here](https://dotnet.microsoft.com/en-us/download/dotnet/6.0).

## Further steps 

On Windows, check whether `sqlite3.dll` is under `bin\Release\net8.0\win-x64\publish`  folder. If not, copy from `bin\Release\net8.0\win-x64\` to `publish` folder.

On Linux, sqlite3 should be installed into system with following command

for Ubuntu

```
sudo apt install sqlite3
```

for CSIRO cluster

```
module load sqlite/3.35.5
```

Asp .netcoreapp3 requires `openssl 1.0`. 

`openssl 3.0` is installed in the Ubuntu 22.04 by default and should be removed and reinstalled with `openssl 1.0`.

