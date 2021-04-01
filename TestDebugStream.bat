@echo off
IF [%1] == [] GOTO Usage
cd Test.ServerStream\bin\debug\net461
start Test.ServerStream.exe
TIMEOUT 3 > NUL
cd ..\..\..\..

cd Test.ClientStream\bin\debug\net461
FOR /L %%i IN (1,1,%1) DO (
ECHO Starting client %%i
start Test.ClientStream.exe
TIMEOUT 1 > NUL
)
cd ..\..\..\..
@echo on
EXIT /b

:Usage
ECHO Specify the number of client nodes to start.
@echo on
EXIT /b
