#!/bin/bash
dotnet clean IronScheme.Tests/IronScheme.Tests.csproj -tl:off -c Release 
dotnet build IronScheme.Tests/IronScheme.Tests.csproj -tl:off -c Release -f net9.0 --disable-build-servers --no-incremental --force

cd IronScheme.Console/bin/Release/net9.0/
export ISWD=$PWD
export TESTCORE=1

dotnet test -v d ../../../../IronScheme.Tests/bin/Release/IronScheme.Tests.dll -- NUnit.DefaultTestNamePattern="{c}.{m}" NUnit.PreFilter=true NUnit.StopOnError=true
cd ../../../..