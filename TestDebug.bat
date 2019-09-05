@echo off
IF [%1] == [] GOTO Usage
cd TestServer\bin\debug\net452
start TestServer.exe
TIMEOUT 3 > NUL
cd ..\..\..\..

cd TestClient\bin\debug\net452
FOR /L %%i IN (1,1,%1) DO (
ECHO Starting client %%i
start TestClient.exe
TIMEOUT 1 > NUL
)
cd ..\..\..\..
@echo on
EXIT /b

:Usage
ECHO Specify the number of client nodes to start.
@echo on
EXIT /b
