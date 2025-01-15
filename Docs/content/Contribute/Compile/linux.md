---
title: "Linux"
draft: false
---

Apsim may be built with the .NET SDK - currently version 6.0 is required. When building the solution, assemblies for all projects will be compiled to the bin/ directory. The exact location of a given file will depend upon how it is built - e.g. debug vs release configuration.

1. Install the .NET Core 6.0 SDK. The [dotnet-install script](https://docs.microsoft.com/en-us/dotnet/core/tools/dotnet-install-script) is a simple way to do this. Otherwise, consult [this page](https://docs.microsoft.com/en-us/dotnet/core/install/linux)

2. Install required packages

	- libsqlite3-dev
	- git (required for obtaining the source code)
    - gtk-sharp3
	- libgtksourceview-4-0
	- dotnet-sdk-6.0

3. Obtain source code

	```
	git clone https://github.com/APSIMInitiative/ApsimX
	```

4. Build the solution

	```bash
	dotnet build ApsimX.sln            # (debug mode)
	dotnet build -c Debug ApsimX.sln   # (debug mode)
	dotnet build -c Release ApsimX.sln # (release mode)
	```

5. Run apsim

    The outputs may be found under ApsimX/bin. If built in debug mode, they will be in `bin/Debug/net8.0/`. If built in release mode, they will be in `bin/Release/net8.0/`.

	The entrypoint program for the user interface is called `ApsimNG`.

	The CLI has two "main" entrypoints - `Models` and `apsim`. `Models` may be used to run .apsimx files. `apsim` accepts multiple verb arguments, but `apsim run` will function identically to an invocation of `Models`.

## Common Problems

When running apsim:

```
System.DllNotFoundException: Unable to load shared library 'sqlite3' or one of its dependencies. In order to help diagnose loading problems, consider setting the LD_DEBUG environment variable: libsqlite3: cannot open shared object file: No such file or directory
```

This error can occur on Debian (and its derivatives) when the sqlite3 package is installed. This package typically provides a file named `libsqlite3.so.0` or similar, but apsim is looking for `libsqlite3.so`. This file is provided by the `libsqlite3-dev` package, so installing this package should fix the problem. Otherwise, creating a symlink to `libsqlite3.so.0` called `libsqlite3.so` should also fix the problem.

---

When attempting to install apsim from our binary package:

```
E: Unable to locate package dotnet-runtime-6.0
```

This package is not included in the official Debian repositories. You will need to follow the instructions on [this page](https://docs.microsoft.com/en-us/dotnet/core/install/linux) to install the package from microsoft's repositories.