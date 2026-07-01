.PHONY: build clean test

build:
	dotnet build nuget_mcp.sln

clean:
	dotnet clean nuget_mcp.sln

test:
	git submodule update --init --recursive
	dotnet test nuget_mcp_core.IntegrationTests
