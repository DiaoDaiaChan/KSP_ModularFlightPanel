using System;
using System.Reflection;

class Program {
    static void Main() {
        string dir = @"C:\Program Files (x86)\Steam\steamapps\common\Kerbal Space Program_newmod\KSP_x64_Data\Managed";
        Assembly.LoadFrom(dir + @"\UnityEngine.dll");
        Assembly.LoadFrom(dir + @"\UnityEngine.CoreModule.dll");
        Assembly.LoadFrom(dir + @"\UnityEngine.UI.dll");
        var asm = Assembly.LoadFrom(dir + @"\Assembly-CSharp.dll");
        var type = asm.GetType("KSP.UI.Screens.Flight.NavBall");
        // Print all methods of NavBall that mention VectorAlpha or DrawOrbitalCues
        foreach (var m in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)) {
            if (m.Name.IndexOf("alpha", StringComparison.OrdinalIgnoreCase) >= 0 ||
                m.Name.IndexOf("cue", StringComparison.OrdinalIgnoreCase) >= 0 ||
                m.Name.IndexOf("vector", StringComparison.OrdinalIgnoreCase) >= 0) {
                Console.WriteLine("Method: " + m.Name);
            }
        }
    }
}
