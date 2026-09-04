using System.Data;
using System.Reflection;
using Jailbreak;
using Microsoft.Extensions.Options;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Helpers;

// Exercise the production cache against an in-memory ADO.NET boundary.
// No native server, external database, or additional test framework is needed.
var reads = 0;
var failRead = false;
var reader = Stub.Create<IDataReader>((method, _) => method.Name switch
{
    "Read" => false,
    _ => Stub.Default(method.ReturnType)
});
var parameters = Stub.Create<IDataParameterCollection>((method, _) => Stub.Default(method.ReturnType));
var command = Stub.Create<IDbCommand>((method, _) => method.Name switch
{
    "get_Parameters" => parameters,
    "CreateParameter" => Stub.Create<IDbDataParameter>((m, _) => Stub.Default(m.ReturnType)),
    "ExecuteReader" => Read(),
    "ExecuteNonQuery" => 1,
    _ => Stub.Default(method.ReturnType)
});
var connection = Stub.Create<IDbConnection>((method, _) => method.Name switch
{
    "CreateCommand" => command,
    _ => Stub.Default(method.ReturnType)
});
var databaseType = typeof(ISwiftlyCore).GetProperty("Database")!.PropertyType;
var database = Stub.Create(databaseType, (method, _) => method.Name switch
{
    "GetConnection" => connection,
    _ => Stub.Default(method.ReturnType)
});
var core = Stub.Create<ISwiftlyCore>((method, _) => method.Name switch
{
    "get_Database" => database,
    _ => Stub.Default(method.ReturnType)
});
var cache = new GuardGunsDatabase(core, Options.Create(new UtilsConfig()));

Check(cache.GetSettings(42) == null, "Missing preferences return null");
Check(cache.GetSettings(42) == null && reads == 1, "Missing preferences are queried only once");
cache.GetSettings(43);
Check(reads == 2, "Different players have independent cache entries");
cache.RemoveFromCache(42);
cache.GetSettings(42);
Check(reads == 3, "Disconnect invalidation allows a fresh read");

cache.SaveSettings(42, ItemDefinitionIndex.Ak47, ItemDefinitionIndex.Glock);
var saved = cache.GetSettings(42);
Check(saved?.PrimaryWeapon == ItemDefinitionIndex.Ak47 && reads == 3,
    "Saving replaces a cached miss without another read");
cache.ClearCache();
cache.GetSettings(42);
Check(reads == 4, "Clearing the cache invalidates saved preferences");

failRead = true;
try
{
    cache.GetSettings(99);
    throw new Exception("Expected the database failure to propagate");
}
catch (DataException) { }
failRead = false;
cache.GetSettings(99);
Check(reads == 6, "Failed reads are retried, not cached as missing preferences");
Console.WriteLine("All 7 regression checks passed.");

IDataReader Read()
{
    reads++;
    if (failRead)
        throw new DataException("Simulated unavailable database");
    return reader;
}

static void Check(bool condition, string name)
{
    if (!condition)
        throw new Exception(name);
    Console.WriteLine($"PASS: {name}");
}

public class Stub : DispatchProxy
{
    private Func<MethodInfo, object?[]?, object?> _handler = null!;

    public static T Create<T>(Func<MethodInfo, object?[]?, object?> handler) where T : class =>
        (T)Create(typeof(T), handler);

    public static object Create(Type type, Func<MethodInfo, object?[]?, object?> handler)
    {
        var proxy = (Stub)DispatchProxy.Create(type, typeof(Stub));
        proxy._handler = handler;
        return proxy;
    }

    public static object? Default(Type type) =>
        type != typeof(void) && type.IsValueType ? Activator.CreateInstance(type) : null;

    protected override object? Invoke(MethodInfo? method, object?[]? args) => _handler(method!, args);
}
