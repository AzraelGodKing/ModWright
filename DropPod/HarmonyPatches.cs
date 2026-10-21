using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace SteamCmdHelper.DropPod;

public enum PatchKind { Prefix, Postfix, Transpiler, Finalizer }

/// <summary>A game method a mod changes with Harmony, and how.</summary>
/// <param name="Target">"Namespace.Type.Method" (nested types joined with "+").</param>
/// <param name="CanSkip">A prefix that returns bool, so it can stop the original method (and other mods' prefixes) from running.</param>
public sealed record HarmonyPatchInfo(string Target, PatchKind Kind, bool CanSkip);

/// <summary>
/// Finds the Harmony patches a mod declares with attributes ([HarmonyPatch(typeof(Pawn), "Tick")] on a class, with
/// Prefix/Postfix/Transpiler methods), read from the DLL's metadata without loading it. Patches a mod makes in code at
/// run time can't be seen this way, so this is a hint, not the full picture.
/// </summary>
public static class HarmonyPatches
{
    public static List<HarmonyPatchInfo> Read(string dll)
    {
        var list = new List<HarmonyPatchInfo>();
        try
        {
            using var fs = new FileStream(dll, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var pe = new PEReader(fs);
            if (!pe.HasMetadata) return list;
            var md = pe.GetMetadataReader();
            foreach (var th in md.TypeDefinitions)
            {
                var type = md.GetTypeDefinition(th);
                var classTarget = Target(md, type.GetCustomAttributes(), new PatchTarget());
                foreach (var mh in type.GetMethods())
                {
                    var method = md.GetMethodDefinition(mh);
                    var attrs = method.GetCustomAttributes();
                    var kind = KindOf(md, attrs) ?? KindByName(md.GetString(method.Name));
                    if (kind == null) continue;
                    var t = Target(md, attrs, classTarget.Copy());
                    if (t.Type == null || t.Method == null) continue; // not a declared patch (or one set up in code)
                    list.Add(new HarmonyPatchInfo(t.Type + "." + t.Method, kind.Value, kind == PatchKind.Prefix && ReturnsBool(md, method)));
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or BadImageFormatException or InvalidOperationException) { }
        return list.Distinct().ToList();
    }

    /// <summary>Every declared patch of every mod, by mod key.</summary>
    public static Dictionary<string, List<HarmonyPatchInfo>> ForMods(IEnumerable<ModManifest> mods, CancellationToken ct = default)
    {
        var result = new Dictionary<string, List<HarmonyPatchInfo>>();
        foreach (var m in mods.Where(m => m.Source != ModSource.Official).DistinctBy(m => m.Key))
        {
            ct.ThrowIfCancellationRequested();
            var patches = ModAssemblies.Files(m.Folder).Where(f => !Path.GetFileName(f).Equals("0Harmony.dll", StringComparison.OrdinalIgnoreCase))
                .SelectMany(Read).Distinct().ToList();
            if (patches.Count > 0) result[m.Key] = patches;
        }
        return result;
    }

    sealed class PatchTarget
    {
        public string? Type, Method;
        public PatchTarget Copy() => new() { Type = Type, Method = Method };
    }

    static string? AttributeName(MetadataReader md, CustomAttribute a)
    {
        var ctor = a.Constructor;
        EntityHandle parent;
        if (ctor.Kind == HandleKind.MemberReference) parent = md.GetMemberReference((MemberReferenceHandle)ctor).Parent;
        else if (ctor.Kind == HandleKind.MethodDefinition) parent = md.GetMethodDefinition((MethodDefinitionHandle)ctor).GetDeclaringType();
        else return null;
        return parent.Kind switch
        {
            HandleKind.TypeReference => md.GetString(md.GetTypeReference((TypeReferenceHandle)parent).Name),
            HandleKind.TypeDefinition => md.GetString(md.GetTypeDefinition((TypeDefinitionHandle)parent).Name),
            _ => null,
        };
    }

    static PatchKind? KindOf(MetadataReader md, CustomAttributeHandleCollection attrs)
    {
        foreach (var h in attrs)
            switch (AttributeName(md, md.GetCustomAttribute(h)))
            {
                case "HarmonyPrefix": return PatchKind.Prefix;
                case "HarmonyPostfix": return PatchKind.Postfix;
                case "HarmonyTranspiler": return PatchKind.Transpiler;
                case "HarmonyFinalizer": return PatchKind.Finalizer;
            }
        return null;
    }

    static PatchKind? KindByName(string name) => name switch
    {
        "Prefix" => PatchKind.Prefix,
        "Postfix" => PatchKind.Postfix,
        "Transpiler" => PatchKind.Transpiler,
        "Finalizer" => PatchKind.Finalizer,
        _ => null,
    };

    /// <summary>Adds what the [HarmonyPatch] attributes in <paramref name="attrs"/> say to <paramref name="t"/>.</summary>
    static PatchTarget Target(MetadataReader md, CustomAttributeHandleCollection attrs, PatchTarget t)
    {
        foreach (var h in attrs)
        {
            var a = md.GetCustomAttribute(h);
            if (AttributeName(md, a) != "HarmonyPatch") continue;
            CustomAttributeValue<string> value;
            try { value = a.DecodeValue(new Names()); }
            catch (Exception e) when (e is BadImageFormatException or InvalidOperationException or ArgumentException) { continue; }
            var strings = new List<string>();
            foreach (var arg in value.FixedArguments)
            {
                if (arg.Type == Names.SystemType && arg.Value is string typeName) t.Type ??= Clean(typeName);
                else if (arg.Type == "string" && arg.Value is string s) strings.Add(s);
                else if (arg.Type.StartsWith("enum:", StringComparison.Ordinal) && arg.Value is int methodType)
                    t.Method = methodType switch { 3 => ".ctor", 4 => ".cctor", _ => t.Method }; // MethodType.Constructor / StaticConstructor
            }
            // (string typeName, string methodName) names both; one string is the method.
            if (strings.Count >= 2 && t.Type == null) { t.Type = Clean(strings[0]); t.Method = strings[1]; }
            else if (strings.Count >= 1) t.Method = strings[0];
            foreach (var arg in value.FixedArguments)
                if (arg.Type.StartsWith("enum:", StringComparison.Ordinal) && arg.Value is int mt && t.Method is { } m && !m.StartsWith('.'))
                    t.Method = mt switch { 1 => "get_" + m, 2 => "set_" + m, _ => m }; // MethodType.Getter / Setter
        }
        return t;
    }

    /// <summary>"Verse.Pawn, Assembly-CSharp, Version=..." becomes "Verse.Pawn".</summary>
    static string Clean(string typeName)
    {
        var i = typeName.IndexOf(',');
        return (i >= 0 ? typeName[..i] : typeName).Trim();
    }

    static bool ReturnsBool(MetadataReader md, MethodDefinition method)
    {
        try
        {
            var r = md.GetBlobReader(method.Signature);
            var header = r.ReadSignatureHeader();
            if (header.IsGeneric) r.ReadCompressedInteger();
            r.ReadCompressedInteger(); // parameter count
            return r.ReadSignatureTypeCode() == SignatureTypeCode.Boolean;
        }
        catch (BadImageFormatException) { return false; }
    }

    /// <summary>Gives attribute argument types as plain names; enums are read as int (Harmony's are).</summary>
    sealed class Names : ICustomAttributeTypeProvider<string>
    {
        public const string SystemType = "System.Type";
        public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode == PrimitiveTypeCode.String ? "string" : typeCode.ToString();
        public string GetSystemType() => SystemType;
        public string GetSZArrayType(string elementType) => elementType + "[]";
        public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
        {
            var t = reader.GetTypeDefinition(handle);
            return Named(reader.GetString(t.Namespace), reader.GetString(t.Name));
        }

        public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
        {
            var t = reader.GetTypeReference(handle);
            return Named(reader.GetString(t.Namespace), reader.GetString(t.Name));
        }

        // A constructor parameter of type System.Type is a reference to that class; anything else Harmony takes is an enum.
        static string Named(string ns, string name) => ns == "System" && name == "Type" ? SystemType : "enum:" + name;
        // Both a typeof() value and a boxed argument's type come here by name.
        public string GetTypeFromSerializedName(string name) => name;
        public PrimitiveTypeCode GetUnderlyingEnumType(string type) => PrimitiveTypeCode.Int32;
        public bool IsSystemType(string type) => type == SystemType;
    }
}
