using System;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

public static class CecilPatcher
{
    public static int Main(string[] args)
    {
        if (args.Length != 3)
        {
            Console.Error.WriteLine("Usage: CecilPatcher <Assembly-CSharp.dll> <NROPrivateAddon.dll> <managed-dir>");
            return 2;
        }

        string gamePath = Path.GetFullPath(args[0]);
        string addonPath = Path.GetFullPath(args[1]);
        string managedDir = Path.GetFullPath(args[2]);

        var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(managedDir);
        resolver.AddSearchDirectory(Path.GetDirectoryName(addonPath));

        var rp = new ReaderParameters { ReadWrite = false, AssemblyResolver = resolver };
        var game = AssemblyDefinition.ReadAssembly(gamePath, rp);
        var addon = AssemblyDefinition.ReadAssembly(addonPath, rp);

        var addonType = addon.MainModule.Types.FirstOrDefault(t => t.FullName == "NROPrivateAddon");
        if (addonType == null) throw new Exception("NROPrivateAddon type not found");
        var tick = addonType.Methods.FirstOrDefault(m => m.Name == "Tick" && m.IsStatic && m.Parameters.Count == 0);
        if (tick == null) throw new Exception("NROPrivateAddon.Tick not found");
        var tickRef = game.MainModule.ImportReference(tick);

        var gameEvents = game.MainModule.Types.FirstOrDefault(t => t.FullName == "Mod.GameEvents");
        if (gameEvents == null) throw new Exception("Mod.GameEvents not found");
        var target = gameEvents.Methods.FirstOrDefault(m => m.Name == "OnPaintGameScr" && m.IsStatic && m.HasBody);
        if (target == null) throw new Exception("OnPaintGameScr hook target not found");

        bool already = target.Body.Instructions.Any(i => i.Operand is MethodReference mr && mr.FullName.Contains("NROPrivateAddon::Tick"));
        if (!already)
        {
            var il = target.Body.GetILProcessor();
            var first = target.Body.Instructions.First();
            il.InsertBefore(first, il.Create(OpCodes.Call, tickRef));
        }

        string tmp = gamePath + ".patched";
        game.Write(tmp, new WriterParameters { WriteSymbols = false });
        game.Dispose();
        addon.Dispose();
        File.Copy(tmp, gamePath, true);
        File.Delete(tmp);
        Console.WriteLine("Patched: " + gamePath);
        return 0;
    }
}
