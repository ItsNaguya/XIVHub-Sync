using System;
using System.Reflection;
class Program {
    static void Main() {
        var asm = Assembly.LoadFile(Environment.ExpandEnvironmentVariables(@"%AppData%\XIVLauncher\addon\Hooks\dev\Dalamud.Bindings.ImGui.dll"));
        foreach(var t in asm.GetExportedTypes()) {
            Console.WriteLine(t.Namespace + " " + t.Name);
            if(t.Name == "ImGui") {
                Console.WriteLine("FOUND IMGUI IN: " + t.Namespace);
            }
        }
    }
}
