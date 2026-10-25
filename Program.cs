using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

class Program
{
    static ModuleDefinition module = null!;
    static int _applied = 0;
    const string MSG = "\u4e3a\u9075\u5b88\u76f8\u5173\u6cd5\u5f8b\u6cd5\u89c4\uff0cPCL \u6682\u65f6\u4e0d\u518d\u63d0\u4f9b MC \u4e0b\u8f7d\u529f\u80fd\uff0c\u975e\u5e38\u62b1\u6b49 qwq\u2026\u2026";
    const string VPATH = @"E:\Projects\PCL\本体代码\Plain Craft Launcher 2\Modules\Minecraft\ModModpack.vb";

    static void Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        try
        {
            Run(args);
        }
        catch (Exception ex)
        {
            Console.WriteLine("[X] 失败：" + ex.Message);
            Environment.ExitCode = 1;
        }
    }

    static void Run(string[] args)
    {
        // ---- one-click mode: no "--" switches ----
        bool toolMode = args.Length > 0 && args[0].StartsWith("--");
        if (toolMode)
        {
            RunTool(args);
            return;
        }

        // ---- locate target ----
        string? target = args.Length > 0 ? args[0] : null;
        if (target == null)
        {
            string cwd = System.IO.Directory.GetCurrentDirectory();
            string guess = System.IO.Path.Combine(cwd, "Plain Craft Launcher 2.exe");
            if (!System.IO.File.Exists(guess))
            {
                Console.WriteLine("[X] Plain Craft Launcher 2.exe not found in current directory.");
                Console.WriteLine("[X] Put this tool next to the PCL exe, or drag the exe onto this tool.");
                Environment.Exit(2);
            }
            target = guess;
        }
        if (!System.IO.File.Exists(target))
        {
            Console.WriteLine("[X] File not found: " + target);
            Environment.Exit(2);
        }

        // ---- kill running PCL ----
        try
        {
            foreach (var p in System.Diagnostics.Process.GetProcessesByName("Plain Craft Launcher 2"))
                try { p.Kill(); p.WaitForExit(3000); } catch { }
        }
        catch { }

        // ---- backup ----
        string dir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(target))!;
        string name = System.IO.Path.GetFileNameWithoutExtension(target);
        string bak = System.IO.Path.Combine(dir, name + ".bak");
        try { if (System.IO.File.Exists(bak)) System.IO.File.Delete(bak); } catch { }
        System.IO.File.Copy(target, bak);
        Console.WriteLine("[1/3] Backup: " + bak);

        // ---- patch ----
        string tmpOut = System.IO.Path.Combine(dir, name + ".patched");
        try { if (System.IO.File.Exists(tmpOut)) System.IO.File.Delete(tmpOut); } catch { }

        string depsDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pclpatch_deps");
        module = ModuleDefinition.ReadModule(target, new ReaderParameters { ReadWrite = false });
        ExtractDeps(depsDir);
        var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(depsDir);
        string[] refDirs =
        {
            @"C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8",
            @"C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.7.2",
            @"C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.X",
            @"C:\Windows\Microsoft.NET\Framework64\v4.0.30319",
            @"C:\Windows\Microsoft.NET\Framework\v4.0.30319",
        };
        foreach (var d in refDirs) if (System.IO.Directory.Exists(d)) resolver.AddSearchDirectory(d);
        module = ModuleDefinition.ReadModule(target, new ReaderParameters { AssemblyResolver = resolver });

        PatchLoaderInit();
        PatchModpackInstallParam();
        PatchModpackInstallNoParam();
        PatchThemeCheckOne();

        if (_applied == 0)
        {
            Console.WriteLine("Nothing to patch (already unrestricted or no restriction found). Target untouched.");
            module.Dispose();
        }
        else
        {
            module.Write(tmpOut);
            if (!System.IO.File.Exists(tmpOut)) throw new Exception("patch output missing");
            Console.WriteLine("[2/3] Patched: MC download + modpack install + all hidden themes");

            // ---- release all handles on the target before replacing ----
            module.Dispose();
            module = null!;

            // ---- replace target ----
            System.IO.File.Copy(tmpOut, target, true);
            try { System.IO.File.Delete(tmpOut); } catch { }
            Console.WriteLine("[3/3] Replaced: " + target);
        }

        // ---- merge Setup.ini (append-only, never destroy user settings) ----
        try
        {
            string pclDir = System.IO.Path.Combine(dir, "PCL");
            string ini = System.IO.Path.Combine(pclDir, "Setup.ini");
            System.IO.Directory.CreateDirectory(pclDir);
            string want = "SystemSystemUpdate:3";
            var lines = System.IO.File.Exists(ini)
                ? new List<string>(System.IO.File.ReadAllLines(ini))
                : new List<string>();
            bool has = lines.Any(l => l.Trim().StartsWith("SystemSystemUpdate:", StringComparison.OrdinalIgnoreCase));
            if (!has) lines.Add(want);
            System.IO.File.WriteAllLines(ini, lines);
            Console.WriteLine("[+] Auto-update disabled (PCL\\Setup.ini). Do NOT click Check-Update.");
        }
        catch (Exception exi) { Console.WriteLine("[!] ini write skipped: " + exi.Message); }

        Console.WriteLine("DONE. Backup kept as " + name + ".bak");
        Environment.Exit(0);
    }

    static void RunTool(string[] args)
    {
        bool refsMode = args[0] == "--refs";
        string exe = args[1];
        string outExe = args.Length > 2 ? args[2] : @"E:\le\biancheng\help-huawei\_out\Plain Craft Launcher 2.exe";
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(outExe)!);

        module = ModuleDefinition.ReadModule(exe, new ReaderParameters { ReadWrite = false });

        if (refsMode)
        {
            Console.WriteLine("== AssemblyReferences ==");
            foreach (var a in module.AssemblyReferences) Console.WriteLine("  " + a.FullName);
            Console.WriteLine("== Resources ==");
            foreach (var r in module.Resources)
            {
                Console.WriteLine("  " + r.Name + "  [" + r.ResourceType + "]");
                if (r is EmbeddedResource er && r.Name.EndsWith(".resources"))
                {
                    try
                    {
                        using var s = er.GetResourceStream();
                        var rr = new System.Resources.ResourceReader(s);
                        foreach (System.Collections.DictionaryEntry e in rr)
                            Console.WriteLine("     - " + e.Key + " : " + (e.Value == null ? "null" : e.Value.GetType().FullName));
                    }
                    catch (Exception ex) { Console.WriteLine("     !! " + ex.Message); }
                }
            }
            Console.WriteLine("== Params with external-type constants ==");
            foreach (var t in module.GetTypes())
                foreach (var m in t.Methods)
                    if (m.HasParameters)
                        foreach (var p in m.Parameters)
                            if (p.HasConstant && p.ParameterType.Scope is AssemblyNameReference anr && anr.Name != "mscorlib" && anr.Name != "Microsoft.VisualBasic")
                                Console.WriteLine($"  {t.FullName}::{m.Name}#{p.Index} : {p.ParameterType.FullName} = {p.Constant}");
            return;
        }

        if (args[0] == "--verify")
        {
            DumpMethod("PCL.PageDownloadInstall", "LoaderInit", 0);
            DumpMethod("PCL.ModSecret", "ThemeCheckOne", 1);
            DumpMethod("PCL.ModModpack", "ModpackInstall", 0);
            DumpMethod("PCL.ModModpack", "ModpackInstall", 3);
            return;
        }

        var depsDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pclpatch_deps");
        ExtractDeps(depsDir);
        var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(depsDir);
        // .NET Framework reference assemblies (for mscorlib 4.0 etc.)
        string[] refDirs =
        {
            @"C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8",
            @"C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.7.2",
            @"C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.X",
            @"C:\Windows\Microsoft.NET\Framework64\v4.0.30319",
        };
        foreach (var d in refDirs) if (System.IO.Directory.Exists(d)) resolver.AddSearchDirectory(d);
        module = ModuleDefinition.ReadModule(exe, new ReaderParameters { AssemblyResolver = resolver });

        PatchLoaderInit();
        if (Environment.GetEnvironmentVariable("SKIP_C") != "1") PatchModpackInstallParam();
        PatchModpackInstallNoParam();
        if (Environment.GetEnvironmentVariable("SKIP_THEME") != "1") PatchThemeCheckOne();

        Console.WriteLine("---- IN-MEMORY Method C dump ----");
        if (Environment.GetEnvironmentVariable("DEBUG_DUMP") == "1") DumpMethod("PCL.ModModpack", "ModpackInstall", 3);

        module.Write(outExe);
        Console.WriteLine("WROTE: " + outExe);
        Console.WriteLine("OK: MC download / modpack install / hidden themes restored.");
        Console.WriteLine("TIP: add SystemSystemUpdate:3 to PCL\\Setup.ini next to the exe to block auto-update.");
        Environment.Exit(0);
    }

    static void DumpMethod(string typeName, string name, int pc)
    {
        var t = module.GetType(typeName);
        if (t == null) { Console.WriteLine("!! type not found " + typeName); return; }
        var m = t.Methods.FirstOrDefault(x => x.Name == name && x.Parameters.Count == pc);
        if (m == null || !m.HasBody) { Console.WriteLine("!! method not found " + typeName + "::" + name + "/" + pc); return; }
        try
        {
            var b = m.Body;
            Console.WriteLine($"== {typeName}::{name}/{pc}  maxstack={b.MaxStackSize} codesize={b.CodeSize} ==");
            foreach (var i in b.Instructions)
                Console.WriteLine("  " + i);
            foreach (var h in b.ExceptionHandlers)
                Console.WriteLine($"  EH {h.HandlerType}: try[{h.TryStart} .. {h.TryEnd}] handler[{h.HandlerStart} .. {h.HandlerEnd}]");
        }
        catch (Exception ex)
        {
            Console.WriteLine("  !! BODY READ ERROR: " + ex.GetType().Name + " : " + ex.Message);
        }
        Console.WriteLine();
    }

    static void ExtractDeps(string dir)
    {
        try { if (System.IO.Directory.Exists(dir)) System.IO.Directory.Delete(dir, true); } catch { }
        System.IO.Directory.CreateDirectory(dir);
        var res = module.Resources.OfType<EmbeddedResource>().FirstOrDefault(r => r.Name == "PCL.Resources.resources");
        if (res == null) { Console.WriteLine("!! PCL.Resources.resources not found"); return; }
        using var s = res.GetResourceStream();
        var rr = new System.Resources.ResourceReader(s);
        foreach (System.Collections.DictionaryEntry e in rr)
        {
            if (e.Value is byte[] b)
            {
                var name = e.Key?.ToString() ?? "";
                if (name.Length == 0) continue;
                var safe = name.Replace('/', '_').Replace('\\', '_');
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, safe + ".dll"), b);
                Console.WriteLine("  extract " + safe + ".dll (" + b.Length + ")");
            }
        }
    }

    // ---- reference lookup ----
    static MethodReference Ref(string typeName, string name, int pc, Func<MethodReference, bool>? pred = null)
    {
        foreach (var t in module.GetTypes())
        {
            if (!t.HasMethods) continue;
            foreach (var m in t.Methods)
            {
                if (!m.HasBody) continue;
                foreach (var ins in m.Body.Instructions)
                {
                    if (ins.Operand is MethodReference mr && mr.Name == name && mr.Parameters.Count == pc &&
                        (mr.DeclaringType.Name == typeName || mr.DeclaringType.FullName == typeName) &&
                        (pred == null || pred(mr)))
                        return mr;
                }
            }
        }
        throw new Exception($"ref not found {typeName}::{name}/{pc}");
    }

    static Instruction C(ILProcessor il, OpCode op) => il.Create(op);
    static Instruction C(ILProcessor il, OpCode op, string s) => il.Create(op, s);
    static Instruction C(ILProcessor il, OpCode op, int i) => il.Create(op, i);
    static Instruction C(ILProcessor il, OpCode op, TypeReference t) => il.Create(op, t);
    static Instruction C(ILProcessor il, OpCode op, MethodReference m) => il.Create(op, m);
    static Instruction C(ILProcessor il, OpCode op, FieldReference f) => il.Create(op, f);
    static Instruction C(ILProcessor il, OpCode op, VariableDefinition v) => il.Create(op, v);
    static Instruction C(ILProcessor il, OpCode op, Instruction t) => il.Create(op, t);

    static void InsertSeq(ILProcessor il, Instruction anchor, IEnumerable<Instruction> seq)
    {
        var cur = anchor;
        foreach (var i in seq) { il.InsertAfter(cur, i); cur = i; }
    }

    // ---- Patch 4: ModSecret.ThemeCheckOne -> always true (unlock all hidden themes) ----
    static void PatchThemeCheckOne()
    {
        var ms = module.GetType("PCL.ModSecret") ?? throw new Exception("ModSecret not found");
        var m = ms.Methods.FirstOrDefault(x => x.Name == "ThemeCheckOne" && x.Parameters.Count == 1)
            ?? throw new Exception("ThemeCheckOne not found (unknown PCL version)");
        var body = m.Body;
        var il = body.GetILProcessor();

        // idempotency: first two instrs already ldc.i4.1; ret ?
        if (body.Instructions.Count >= 2 &&
            body.Instructions[0].OpCode == OpCodes.Ldc_I4_1 &&
            body.Instructions[1].OpCode == OpCodes.Ret)
        {
            Console.WriteLine("[4] ThemeCheckOne: already unlocked, skip");
            return;
        }

        var first = body.Instructions[0];
        var retTrue = il.Create(OpCodes.Ldc_I4_1);
        var ret = il.Create(OpCodes.Ret);
        il.InsertBefore(first, retTrue);
        il.InsertAfter(retTrue, ret);

        Console.WriteLine("[4] ThemeCheckOne -> always true (all hidden themes unlocked)");
        _applied++;
    }

    // ---- Patch 1: PageDownloadInstall.LoaderInit ----
    static void PatchLoaderInit()
    {
        var pdi = module.GetType("PCL.PageDownloadInstall") ?? throw new Exception("PageDownloadInstall not found");
        var m = pdi.Methods.First(x => x.Name == "LoaderInit");
        var body = m.Body;
        var il = body.GetILProcessor();

        // idempotency: already passing the real loader?
        bool hasRealLoader = body.Instructions.Any(i => i.OpCode == OpCodes.Ldsfld &&
            i.Operand is FieldReference fr && fr.Name == "DlClientListLoader");
        var start = body.Instructions.FirstOrDefault(i => i.OpCode == OpCodes.Ldstr && (string?)i.Operand == "关闭下载");
        if (start == null)
        {
            if (hasRealLoader) { Console.WriteLine("[1] LoaderInit: already unrestricted, skip"); return; }
            throw new Exception("LoaderInit: anchor not found (unknown PCL structure, aborting safely)");
        }

        Instruction? end = null;
        for (var i = start; i != null; i = i.Next)
        {
            if (i.OpCode == OpCodes.Newobj && i.Operand is MethodReference mr &&
                mr.Name == ".ctor" && mr.DeclaringType.Name.StartsWith("LoaderTask`2"))
            { end = i; break; }
        }
        if (end == null) throw new Exception("LoaderTask ctor not found in LoaderInit");

        var dlField = module.GetType("PCL.ModDownload").Fields.First(f => f.Name == "DlClientListLoader");

        var rem = new List<Instruction>();
        for (var i = start; ; i = i!.Next!) { rem.Add(i); if (i == end) break; }

        var load = il.Create(OpCodes.Ldsfld, dlField);
        il.InsertBefore(start, load);
        foreach (var i in rem) il.Remove(i);

        Console.WriteLine("[1] LoaderInit patched");
        _applied++;
    }

    // ---- Patch 2: ModModpack.ModpackInstall(File, InstanceName, Logo) -> restore Select Case ----
    static void PatchModpackInstallParam()
    {
        var mm = module.GetType("PCL.ModModpack") ?? throw new Exception("ModModpack not found");
        var m = mm.Methods.First(x => x.Name == "ModpackInstall" && x.Parameters.Count == 3);
        var body = m.Body;
        var il = body.GetILProcessor();

        // idempotency: dispatch already rebuilt? (patched version has InstallPack* calls in this method)
        if (body.Instructions.Any(i => i.OpCode == OpCodes.Call && i.Operand is MethodReference cmr &&
             (cmr.Name == "InstallPackCurseForge" || cmr.Name == "InstallPackCompress")))
        {
            Console.WriteLine("[2] ModpackInstall(File,...): already patched, skip");
            return;
        }

        var anchor = body.Instructions.FirstOrDefault(i => i.OpCode == OpCodes.Ldstr && (string?)i.Operand == MSG)
            ?? throw new Exception("ModpackInstall(3): restriction anchor not found (unknown PCL version)");
        var fin = body.ExceptionHandlers.Where(h => h.HandlerType == ExceptionHandlerType.Finally)
                                       .OrderByDescending(h => h.HandlerStart.Offset).First();
        var finallyStart = fin.TryEnd;

        // remove everything strictly after anchor up to finallyStart (the injected newobj Exception + throw)
        var toRemove = new List<Instruction>();
        for (var i = anchor.Next; i != null && i != finallyStart; i = i.Next) toRemove.Add(i);

        // rewrite anchor into: ldloc.s PackType(4)
        anchor.OpCode = OpCodes.Ldloc_S;
        anchor.Operand = body.Variables[4];

        foreach (var i in toRemove) il.Remove(i);

        // refs
        var zipType = body.Variables[1].VariableType;               // ZipArchive
        var baseType = body.Variables[2].VariableType;              // string
        var retType = m.ReturnType;                                 // LoaderCombo`1<string>
        var info = Ref("Logger", "Info", 3);
        var ipCurse = mm.Methods.First(x => x.Name == "InstallPackCurseForge");
        var ipHMCL = mm.Methods.First(x => x.Name == "InstallPackHMCL");
        var ipMMC = mm.Methods.First(x => x.Name == "InstallPackMMC");
        var ipMCBBS = mm.Methods.First(x => x.Name == "InstallPackMCBBS");
        var ipModrinth = mm.Methods.First(x => x.Name == "InstallPackModrinth");
        var ipLauncher = mm.Methods.First(x => x.Name == "InstallPackLauncherPack");
        var ipCompress = mm.Methods.First(x => x.Name == "InstallPackCompress");

        // AFTER (after endfinally): ldloc.0 ; ret
        var endFinally = body.Instructions.Last(i => i.OpCode == OpCodes.Endfinally);
        var after = il.Create(OpCodes.Ldloc_0);
        var afterRet = il.Create(OpCodes.Ret);
        var tmp = il.Create(OpCodes.Nop);
        il.Replace(endFinally, tmp);            // placeholder to allow inserting after
        il.InsertAfter(tmp, after);
        il.InsertAfter(after, afterRet);
        il.Replace(tmp, endFinally);            // restore endfinally position (before after)

        fin.HandlerEnd = after;

        // build dispatch: stack has PackType
        var code = new List<Instruction>();
        var V = body.Variables;
        var def = V[4].VariableType; // int32

        // helper to emit a case body
        List<Instruction> Case(string logMsg, MethodReference target)
        {
            var list = new List<Instruction>();
            list.Add(il.Create(OpCodes.Ldstr, logMsg));
            list.Add(il.Create(OpCodes.Ldc_I4_0));
            list.Add(il.Create(OpCodes.Ldstr, VPATH));
            list.Add(il.Create(OpCodes.Call, info));
            list.Add(il.Create(OpCodes.Ldarg_0));
            list.Add(il.Create(OpCodes.Ldloc_1));
            // ArchiveBaseFolder only if target has >=3 params
            if (target.Parameters.Count >= 3) list.Add(il.Create(OpCodes.Ldloc_2));
            if (target.Parameters.Count >= 4) list.Add(il.Create(OpCodes.Ldarg_1));
            if (target.Parameters.Count >= 5) list.Add(il.Create(OpCodes.Ldarg_2));
            list.Add(il.Create(OpCodes.Call, target));
            list.Add(il.Create(OpCodes.Stloc_0));
            list.Add(il.Create(OpCodes.Leave, after));
            return list;
        }

        var defCode = Case("整合包种类：未能识别，假定为压缩包", ipCompress);
        var c0 = Case("整合包种类：CurseForge", ipCurse);
        var c1 = Case("整合包种类：HMCL", ipHMCL);
        var c2 = Case("整合包种类：MMC", ipMMC);
        var c3 = Case("整合包种类：MCBBS", ipMCBBS);
        var c4 = Case("整合包种类：Modrinth", ipModrinth);
        var c9 = Case("整合包种类：带启动器的压缩包", ipLauncher);

        var sw = il.Create(OpCodes.Switch, new Instruction[10]);
        // after switch fallthrough -> default
        foreach (var i in defCode) code.Add(i);
        var j0 = c0[0]; foreach (var i in c0) code.Add(i);
        var j1 = c1[0]; foreach (var i in c1) code.Add(i);
        var j2 = c2[0]; foreach (var i in c2) code.Add(i);
        var j3 = c3[0]; foreach (var i in c3) code.Add(i);
        var j4 = c4[0]; foreach (var i in c4) code.Add(i);
        var j9 = c9[0]; foreach (var i in c9) code.Add(i);
        sw.Operand = new[] { j0, j1, j2, j3, j4, defCode[0], defCode[0], defCode[0], defCode[0], j9 };

        InsertSeq(il, anchor, code.Prepend(sw));
        // but sw must come immediately after anchor; build final order manually
        // (InsertSeq inserts sw first then code)
        
        body.MaxStackSize = 8;
        Console.WriteLine("[2] ModpackInstall(File,...) patched");
        _applied++;
    }

    // ---- Patch 3: ModModpack.ModpackInstall() (no param) ----
    static void PatchModpackInstallNoParam()
    {
        var mm = module.GetType("PCL.ModModpack") ?? throw new Exception("ModModpack not found");
        var m = mm.Methods.FirstOrDefault(x => x.Name == "ModpackInstall" && x.Parameters.Count == 0)
            ?? throw new Exception("ModpackInstall(0) not found (unknown PCL version)");
        // idempotency: already rebuilt?
        if (m.HasBody && m.Body.Instructions.Any(i => i.OpCode == OpCodes.Ldstr && (string?)i.Operand == "选择整合包压缩文件"))
        {
            Console.WriteLine("[3] ModpackInstall(): already patched, skip");
            return;
        }

        var nb = new MethodBody(m);
        nb.InitLocals = true;
        var vFile = new VariableDefinition(module.TypeSystem.String);
        nb.Variables.Add(vFile);
        nb.MaxStackSize = 8;
        m.Body = nb;
        var il = nb.GetILProcessor();

        var selectFile = Ref("Dialogs", "SelectFile", 4);
        var firstOrDefault = Ref("Enumerable", "FirstOrDefault", 1);
        var opImplicit = Ref("OneOf`2", "op_Implicit", 1,
            mr => mr.DeclaringType is GenericInstanceType git &&
                  git.GenericArguments[0].FullName.StartsWith("System.Collections.Generic.IEnumerable`1<System.ValueTuple`2<System.String[],System.String>>") &&
                  mr.Parameters[0].ParameterType is GenericParameter gp && gp.Position == 0);
        var nullCtor = Ref("Nullable`1", ".ctor", 1,
            mr => mr.DeclaringType is GenericInstanceType git && git.GenericArguments[0].FullName.StartsWith("MeloongCore.OneOf`2"));
        var tupleCtor = Ref("ValueTuple`2", ".ctor", 2,
            mr => mr.DeclaringType is GenericInstanceType git &&
                  git.GenericArguments[0].FullName == "System.String[]" && git.GenericArguments[1].FullName == "System.String");
        var tupleType = tupleCtor.DeclaringType;   // ValueTuple`2<string[],string>

        var isNullOrEmpty = Ref("String", "IsNullOrEmpty", 1);
        var runInThread = Ref("ModBase", "RunInThread", 1);
        var actionCtor = Ref("Action", ".ctor", 2);

        var closure = mm.NestedTypes.FirstOrDefault(t => t.Name == "_Closure$__0-0")
            ?? throw new Exception("closure _Closure$__0-0 not found (unknown PCL version)");
        var closureCtor = closure.Methods.First(x => x.IsConstructor);
        var lambda = closure.Methods.First(x => x.Name == "_Lambda$__0");
        var fileField = closure.Fields.First(f => f.Name == "$VB$Local_File");

        void A(Instruction i) => il.Append(i);

        // Dialogs.SelectFile("选择整合包压缩文件", False, Nothing, filter:={({"rar","zip","mrpack"},"整合包文件")}).FirstOrDefault()
        A(C(il, OpCodes.Ldstr, "选择整合包压缩文件"));
        A(C(il, OpCodes.Ldc_I4_0));
        A(C(il, OpCodes.Ldnull));
        A(C(il, OpCodes.Ldc_I4_1));
        A(C(il, OpCodes.Newarr, tupleType));
        A(C(il, OpCodes.Dup));
        A(C(il, OpCodes.Ldc_I4_0));
        A(C(il, OpCodes.Ldc_I4_3));
        A(C(il, OpCodes.Newarr, module.TypeSystem.String));
        A(C(il, OpCodes.Dup)); A(C(il, OpCodes.Ldc_I4_0)); A(C(il, OpCodes.Ldstr, "rar")); A(C(il, OpCodes.Stelem_Ref));
        A(C(il, OpCodes.Dup)); A(C(il, OpCodes.Ldc_I4_1)); A(C(il, OpCodes.Ldstr, "zip")); A(C(il, OpCodes.Stelem_Ref));
        A(C(il, OpCodes.Dup)); A(C(il, OpCodes.Ldc_I4_2)); A(C(il, OpCodes.Ldstr, "mrpack")); A(C(il, OpCodes.Stelem_Ref));
        A(C(il, OpCodes.Ldstr, "整合包文件"));
        A(C(il, OpCodes.Newobj, tupleCtor));
        A(C(il, OpCodes.Stelem_Any, tupleType));
        A(C(il, OpCodes.Call, opImplicit));
        A(C(il, OpCodes.Newobj, nullCtor));
        A(C(il, OpCodes.Call, selectFile));
        A(C(il, OpCodes.Call, firstOrDefault));
        A(C(il, OpCodes.Stloc, vFile));

        // If String.IsNullOrEmpty(File) Then Return
        A(C(il, OpCodes.Ldloc, vFile));
        A(C(il, OpCodes.Call, isNullOrEmpty));
        var runLabel = il.Create(OpCodes.Nop);
        var br = il.Create(OpCodes.Brfalse, runLabel);
        A(br);
        A(C(il, OpCodes.Ret));
        A(runLabel);

        // RunInThread(closure with File)
        A(C(il, OpCodes.Newobj, closureCtor));
        A(C(il, OpCodes.Dup));
        A(C(il, OpCodes.Ldloc, vFile));
        A(C(il, OpCodes.Stfld, fileField));
        A(C(il, OpCodes.Dup));
        A(C(il, OpCodes.Ldftn, lambda));
        A(C(il, OpCodes.Newobj, actionCtor));
        A(C(il, OpCodes.Call, runInThread));
        A(C(il, OpCodes.Ret));

        Console.WriteLine("[3] ModpackInstall() patched");
        _applied++;
    }
}