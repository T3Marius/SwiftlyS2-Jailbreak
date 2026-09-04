# Regression checks

Run from the repository root:

```powershell
dotnet run --project Tests/RegressionChecks/RegressionChecks.csproj -c Release
```

This executable links the production guard-gun database code and tests its cache
through fake ADO.NET and Swiftly interfaces. It checks missing preferences,
per-player isolation, disconnect and global invalidation, saves replacing misses,
and retrying failed reads. Failures exit with an exception and a nonzero code.

It does not load a CS2 server or connect to a real database. Native entity behavior,
SQL dialect compatibility, and gameplay need integration checks on a server.
