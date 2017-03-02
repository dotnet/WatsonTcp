# Watson TCP

[![][nuget-img]][nuget]

[nuget]:     https://www.nuget.org/packages/WatsonTcp/
[nuget-img]: https://badge.fury.io/nu/Object.svg

A simple C# async TCP server and client with integrated framing for reliable transmission and receipt of data. 

## Test App
A test project for both client and server are included which will help you understand and exercise the class library.

## Running under Mono
Watson works well in Mono environments to the extent that we have tested it. It is recommended that when running under Mono, you execute the containing EXE using --server and after using the Mono Ahead-of-Time Compiler (AOT).

NOTE: Windows accepts '0.0.0.0' as an IP address representing any interface.  On Mac and Linux you must be specified ('127.0.0.1' is also acceptable, but '0.0.0.0' is NOT).

## Contributions
Thanks to @brudo for his contributions to add async support to WatsonTcp (pushed in v1.0.7).

```
mono --aot=nrgctx-trampolines=8096,nimt-trampolines=8096,ntrampolines=4048 --server myapp.exe
mono --server myapp.exe
```
