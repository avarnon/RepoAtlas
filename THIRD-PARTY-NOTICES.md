# Third-Party Notices

RepoAtlas is licensed under the MIT License (see [LICENSE](LICENSE)). It depends on the
third-party packages below. This file records their licenses and, for the one package whose
license imposes notice-retention obligations, exists so those obligations are met before
RepoAtlas is first distributed as a binary, NuGet package, or `dotnet tool`.

## Direct dependencies

| Package | License |
| --- | --- |
| [LibGit2Sharp](https://github.com/libgit2/libgit2sharp) | MIT |
| [LibGit2Sharp.NativeBinaries](https://github.com/libgit2/libgit2sharp.nativebinaries) (transitive; bundles libgit2) | See [libgit2](#libgit2-libgit2sharpnativebinaries) below |
| [Microsoft.Extensions.Hosting](https://github.com/dotnet/runtime) | MIT |
| [ModelContextProtocol](https://github.com/modelcontextprotocol/csharp-sdk) | Apache-2.0 |
| [OpenTelemetry.Exporter.OpenTelemetryProtocol](https://github.com/open-telemetry/opentelemetry-dotnet) | Apache-2.0 |
| [OpenTelemetry.Extensions.Hosting](https://github.com/open-telemetry/opentelemetry-dotnet) | Apache-2.0 |
| [YamlDotNet](https://github.com/aaubry/YamlDotNet) | MIT |

Test-only (not distributed): coverlet.collector (MIT), Microsoft.NET.Test.Sdk (MIT), xunit /
xunit.runner.visualstudio (Apache-2.0), Xunit.SkippableFact (MS-PL, per its nuspec — transitively
depends on `xunit.extensibility.execution` 2.4.0 and `Validation` 2.6.68, both also MS-PL),
OpenTelemetry.Exporter.InMemory (Apache-2.0).

## libgit2 (LibGit2Sharp.NativeBinaries)

`LibGit2Sharp` depends on `LibGit2Sharp.NativeBinaries`, which bundles the native `libgit2`
library. `libgit2` itself is licensed under **GPLv2 with a linking exception**:

> In addition to the permissions in the GNU General Public License, the authors give you
> unlimited permission to link the compiled version of this library into combinations with
> other programs, and to distribute those combinations without any restriction coming from the
> use of this file.

The linking exception means RepoAtlas linking against libgit2 does not require RepoAtlas itself
to be GPL-licensed, and imposes no source-offer obligation on RepoAtlas. `libgit2` also bundles
several components under their own permissive licenses (zlib, ISC, PCRE2/BSD, MIT, BSD, and
portions of OpenSSL under its original license, plus LGPL-2.1 for an optional Windows WinHTTP
header). The complete text is included with the package at
`libgit2sharp.nativebinaries/<version>/libgit2/libgit2.license.txt` in the NuGet cache, and is
reproduced in full at <https://github.com/libgit2/libgit2sharp.nativebinaries>.

## Attribution

This notice, together with each package's own bundled license text, satisfies the attribution
and notice-retention terms of the MIT-licensed and Apache-2.0-licensed dependencies above
(MIT's "include the copyright notice" condition; Apache-2.0 §4(d)'s notice-retention
requirement) for any future distribution of a RepoAtlas binary or package.
