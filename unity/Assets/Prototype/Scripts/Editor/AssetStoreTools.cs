using System;
using System.IO;
using System.Linq.Expressions;
using System.Reflection;
using UnityEditor;
using UnityEditor.PackageManager.UI;

// Lists and downloads the signed-in account's Asset Store packs ("My
// Assets") without the Package Manager window, so they can be fetched from
// the command line. Uses Unity's internal Package Manager services through
// reflection: it can break on a Unity upgrade, and then the Package
// Manager's My Assets tab is the way in.
public static class AssetStoreTools
{
    public const string PurchasesFile = "Temp/asset-store-purchases.json";
    const string Ns = "UnityEditor.PackageManager.UI.Internal.";
    static readonly Assembly Asm = typeof(Window).Assembly;

    static object Service(string interfaceName)
    {
        var container = Asm.GetType(Ns + "ServicesContainer");
        var instance = container.BaseType.GetProperty("instance").GetValue(null);
        return container.GetMethod("Resolve").MakeGenericMethod(Asm.GetType(Ns + interfaceName)).Invoke(instance, null);
    }

    // Writes the first 100 purchases to Temp/asset-store-purchases.json when the request returns.
    public static void ListPurchases()
    {
        File.WriteAllText(PurchasesFile, "pending");
        var api = Service("IAssetStoreRestAPI");
        var argsType = Asm.GetType(Ns + "PurchasesQueryArgs");
        var args = Activator.CreateInstance(argsType, 0, 100, "", null);
        var onOk = Callback(Asm.GetType(Ns + "AssetStorePurchases"), r => File.WriteAllText(PurchasesFile, EditorJsonUtility.ToJson(r, true)));
        var onError = Callback(Asm.GetType(Ns + "UIError"), e => File.WriteAllText(PurchasesFile, "error: " + e));
        Asm.GetType(Ns + "IAssetStoreRestAPI").GetMethod("GetPurchases").Invoke(api, new[] { args, onOk, onError });
    }

    // Writes each pack's store details to Temp/asset-store-<id>.json when they arrive.
    public static void ProductDetails(params long[] productIds)
    {
        var api = Service("IAssetStoreRestAPI");
        var get = Asm.GetType(Ns + "IAssetStoreRestAPI").GetMethod("GetProductDetail");
        var argType = get.GetParameters()[1].ParameterType.GetGenericArguments()[0];
        var errType = get.GetParameters()[2].ParameterType.GetGenericArguments()[0];
        foreach (var id in productIds)
        {
            string file = $"Temp/asset-store-{id}.json";
            File.WriteAllText(file, "pending");
            var onOk = Callback(argType, r => File.WriteAllText(file, MiniJson(r)));
            var onError = Callback(errType, e => File.WriteAllText(file, "error: " + e));
            get.Invoke(api, new[] { (object)id, onOk, onError });
        }
    }

    // Product details come back as a Dictionary<string, object> tree; render it as JSON.
    static string MiniJson(object o)
    {
        switch (o)
        {
            case null: return "null";
            case string s: return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "") + "\"";
            case bool b: return b ? "true" : "false";
            case System.Collections.IDictionary d:
            {
                var parts = new System.Collections.Generic.List<string>();
                foreach (System.Collections.DictionaryEntry e in d) parts.Add(MiniJson(e.Key.ToString()) + ":" + MiniJson(e.Value));
                return "{" + string.Join(",", parts) + "}";
            }
            case System.Collections.IEnumerable list:
            {
                var parts = new System.Collections.Generic.List<string>();
                foreach (var x in list) parts.Add(MiniJson(x));
                return "[" + string.Join(",", parts) + "]";
            }
            case IFormattable f: return f.ToString(null, System.Globalization.CultureInfo.InvariantCulture);
            default: return MiniJson(EditorJsonUtility.ToJson(o));
        }
    }

    // Downloads packs into ~/Library/Unity/Asset Store-5.x. Import them with AssetDatabase.ImportPackage.
    public static void Download(params long[] productIds)
    {
        var manager = Service("IAssetStoreDownloadManager");
        Asm.GetType(Ns + "IAssetStoreDownloadManager").GetMethod("Download").Invoke(manager, new object[] { productIds });
    }

    // Builds an Action<T> for an internal T that forwards to a plain Action<object>.
    static Delegate Callback(Type argType, Action<object> body)
    {
        var p = Expression.Parameter(argType);
        var call = Expression.Invoke(Expression.Constant(body), Expression.Convert(p, typeof(object)));
        return Expression.Lambda(typeof(Action<>).MakeGenericType(argType), call, p).Compile();
    }
}
