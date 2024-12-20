@echo off
setlocal

cd IronScheme.Console\bin\Release\net20
del /q IronScheme.FrameworkPAL.* >nul
del /q IronScheme.Console.* >nul

copy ..\netcoreapp2.1\IronScheme.ConsoleCore.dll . >nul
copy ..\net9.0\IronScheme.ConsoleCore.runtimeconfig.json . >nul

SET ISWD=%CD%
SET TESTCORE=1

dotnet test -v n ..\..\..\..\IronScheme.Tests\bin\Release\IronScheme.Tests.dll --filter "Category=Conformance|SRFI|Other" -- NUnit.DefaultTestNamePattern="{c}.{m}" NUnit.PreFilter=true NUnit.StopOnError=false